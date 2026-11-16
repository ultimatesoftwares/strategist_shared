# Dead DB schema audit — 2026-11-16

Static cross-repo audit of every table in the shared Strategist schema
(`Persistence/AppDbContext.cs`, Postgres `strategist-db`) and the QuestDB
time-series store (`Infrastructure/TimeSeries/Schema.cs`), checked against
all code in the current working trees of:

- `strategist_server` (auth-only REST API)
- `strategist_fleet` (fleetd host daemon)
- `strategist_bot` (trading engine + Bot API + BacktestEngine)
- `strategist_desktop` (Rust GUI — no DB access at all)
- `strategist_mobile` (empty repo)
- `strategist_shared` itself (outside `Persistence/`)

Method: grepped every `DbSet` property, entity class name, `Set<T>()` call,
`Include(...)` navigation, and raw-SQL pattern (`FromSql`, `NpgsqlCommand`,
table-name strings) in each repo, excluding `shared/` submodule copies,
`bin/`, `obj/`, and `target/`. No raw SQL against the Postgres schema exists
anywhere; every access goes through `AppDbContext`, so DbSet/navigation
usage is a complete census.

## Verdict summary

- The 11 tables of the two dead clusters (fleet hub registry + engine
  orchestration) were dropped on 2026-11-16 (migrations below).
- 7 further tables have no consumer today but are **kept by decision**
  (future features) — see the "kept" list below.
- QuestDB `bars` and `ticks` are both live.
- The ASP.NET Identity side tables are framework-managed; keep them.

## Live tables

| Table | Consumers |
|---|---|
| `users` + `AspNetRoles/UserRoles/UserClaims/RoleClaims/UserLogins/UserTokens` | `strategist_server` Identity auth (Users/Register/Token/Roles/Profile controllers); `strategist_bot` `Api/VenuesController.cs` (`Set<ApplicationUser>()` owner lookup) |
| `user_information` | `strategist_server` `Controllers/UsersController.cs` (`Include(u => u.Information)`) |
| `addresses` | `strategist_server` `Controllers/UsersController.cs` (`Include(u => u.Addresses)`) |
| `instruments` | `strategist_bot` `Api/InstrumentsController.cs`, `Hosting/HistoricalDataService.cs` |
| `instrument_catalog` | `strategist_bot` `Api/InstrumentsController.cs`, `Hosting/InstrumentCatalogSync.cs`, `Hosting/VenueDirectory.cs`, `Hosting/HistoricalDataService.cs` |
| `ctrader_venues` | `strategist_bot` `Api/VenuesController.cs`, `Hosting/VenueDirectory.cs`, `Program.cs` |
| `alpaca_venues` | `strategist_bot` `Api/VenuesController.cs`, `Hosting/VenueDirectory.cs` |
| `projects` | `strategist_server` `Controllers/ProjectsController.cs` (moved from the bot 2026-11-16) |
| `project_members` | `strategist_server` `Controllers/ProjectsController.cs` |
| `project_strategy_links` | `strategist_server` `Controllers/ProjectsController.cs` |
| `project_activity` | `strategist_server` `Controllers/ProjectsController.cs` |
| `fleet_terminals` | `strategist_fleet` `Service/DbTerminalConfigStore.cs`, `Program.cs` |

QuestDB: `bars`, `ticks` — written/read by `strategist_bot`
(`Hosting/HistoricalDataService.cs`, `Hosting/FleetHistorySource.cs`,
`src/BacktestEngine/Api/QuestDbMarketData.cs`) via
`Infrastructure/TimeSeries` in this repo.

## Unused tables kept by decision (do NOT drop)

These 7 tables have no consumer in any repo today, but are **kept on
purpose** (2026-11-16 decision) for planned features:

| Table | Entity | Notes / what replaced it |
|---|---|---|
| `orders` | `Order` | Old server order log. The bot's OMS keeps order state in-memory (`Trading.Accounts`, `account.Orders`); nothing touches `db.Orders`. Carries the `DecisionId` FK to `model_decisions` (`20261112003757_AddOrderDecisionLink`), which dies with it. |
| `model_decisions` | `ModelDecision` | Old engine decision journal (incl. `BookJson` column from `20261111110422`). No writer or reader. |
| `strategy_packages` | `StrategyPackage` | Old packaging/publish flow. |
| `backtest_results` | `BacktestResult` | BacktestEngine now journals jobs as JSON (`BacktestJobStore` journal dir) and reads market data from QuestDB; Postgres is not involved. |
| `provision_jobs` | `ProvisionJob` | Both successors avoid it: bot history fetches persist JSON manifests (`Hosting/HistoricalDataService.cs`); fleet's `Service/TerminalProvisioner.cs` uses its own **private in-memory `ProvisionJob` class** (same name, not the entity). |
| `project_documents` | `ProjectDocument` | Never got a consumer after the split. |
| `project_risk_policies` | `ProjectRiskPolicy` | Never got a consumer after the split. |

### Dropped 2026-11-16: fleet hub registry cluster

Migration `20261116072008_DropFleetHubRegistry` (in this repo) drops
`fleet_venues`, `fleet_api_keys`, `fleet_instrument_mappings`,
`fleet_venue_instruments`, and `fleet_venue_registrations`, and removes
their entities, DbSets, and model configuration. Notes:

- `venue_instances.FleetVenueId` survived as a **plain column** (the
  `fleet:{id}` venue-token parsing still read it); only the FK constraint +
  index to `fleet_venue_registrations` were dropped. (Moot since the
  engine-orchestration drop below removed `venue_instances` entirely.)
- The migration has a full `Down()` recreating all five tables.
- Consumer repos need their `shared` submodule pin bumped once this is
  pushed; all three were build-verified against the change
  (server tests 34/34 green).

### Dropped 2026-11-16: engine orchestration cluster

Migration `20261116072817_DropEngineOrchestration` drops
`metatrader_venues`, `strategy_instances`, `venue_instances`,
`feed_instances`, `venue_owner`, and `InstancePositions`, and removes
their entities (`MetaTraderTerminal`, `StrategyInstance`, `VenueInstance`,
`FeedInstance`, `VenueOwner`, `InstancePosition`), the
`StrategyInstanceStatus`/`VenueInstanceStatus`/`VenuePlatform`/
`FeedInstanceStatus` enums, their DbSets and model configuration. Notes:

- The migration breaks the `feed_instances` ↔ `venue_instances` FK cycle
  first, then drops all six tables; full `Down()` included.
- This also deleted the entire SaveChanges coupling layer in
  `AppDbContext` (`CoupleVenueInstances`, `SpawnVenueInstance`,
  `CoupleFeedInstances`, `FeedSourceFor`, `LegsFor`,
  `NormalizedVenueTokens`, `VenueTokenOf`) — it only manipulated these
  entities. `Timestamp()` now only stamps `BaseEntity` rows.
- All three consumer repos build-verified; server tests 34/34 green.

## Dead schema-adjacent code

Still-orphaned code after the drops:

- `strategist_server/Controllers/CrudControllerBase.cs` — generic CRUD base
  with **zero subclasses** in any repo (the entity CRUD controllers it
  served were dropped in the split).
- `strategist_shared/OpenApi/Server/server-v1.json` — snapshot of the old
  monorepo server API (engine state, fleet, historical provisioning,
  instruments). The current `strategist_server` is auth-only; this spec no
  longer matches any running service (the bot/backtest/fleet specs next to
  it are the current ones).

## Already-dropped tables (history, not action items)

Migrations already removed these; a live DB migrated to head does not have
them, but a stale environment might: `trade_signals`
(`20261111182842_DropTradeSignals`), `build_jobs` + `walkforward_runs`
(`20261110205302`), `ibkr_venues` + `hyperliquid_venues` (`20261110073533`),
and the pre-fleet `instrument_mappings` (`20261111234819
RetireInstrumentMappings`). Verify stale environments with `\dt` before
assuming the migration history was applied.

## Cleanup recipe (done 2026-11-16 for the two dead clusters)

1. In this repo: delete the dead entities from `Entities.cs`, their DbSets
   and model configuration from `AppDbContext.cs`, and the SaveChanges
   coupling helpers; then
   `dotnet ef migrations add Drop... --project Persistence`.
   (Done: `20261116072008_DropFleetHubRegistry` and
   `20261116072817_DropEngineOrchestration`.)
2. Build all four consumer repos against the updated submodule before
   committing the migration — this audit is static; a compile is the
   cheap second check.
3. Bump the `shared` submodule pin in `strategist_server`,
   `strategist_fleet`, `strategist_bot`, `strategist_desktop`.
4. Deploy order doesn't matter (no live code references the tables), but
   the migration runs wherever `Database:Migrate=true` — usually the
   server; fleet also opts in.

## Caveats

- **Static analysis only.** Anything outside these repos that talks to
  `strategist-db` directly (Grafana dashboards, ad-hoc ops SQL, an old
  monorepo deployment still running against the same database) is
  invisible here. Check deployed services before dropping.
- Audit covers the current working trees as of 2026-11-16, not any
  historical tags.
- Keep all `AspNet*` Identity tables even where no code references them
  by name — ASP.NET Identity manages them internally (tokens, logins,
  claims).

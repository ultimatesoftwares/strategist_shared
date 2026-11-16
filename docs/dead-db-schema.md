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
- All 7 tables that were kept by decision are now hooked up (6 on
  2026-11-16, `model_decisions` later the same day — see "Live tables").
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
| `project_documents` | `strategist_server` `Controllers/ProjectDocumentsController.cs` (hooked 2026-11-16; files under `Server:ProjectsDir`) |
| `project_risk_policies` | `strategist_server` `Controllers/ProjectRiskPoliciesController.cs` (hooked 2026-11-16; versioned rules, single-active invariant) |
| `strategy_packages` | `strategist_server` `Controllers/PackagesController.cs` (hooked 2026-11-16; content-addressed `.sgpkg` registry under `Server:PackagesDir`, promotion ladder) |
| `backtest_results` | `strategist_bot` `src/BacktestEngine/Api/BacktestResultMirror.cs` (hooked 2026-11-16; fail-open mirror of the job journal, on when "database" is enabled) |
| `orders` | `strategist_bot` `src/Bot/Hosting/OrderLog.cs` (hooked 2026-11-16; fills/cancels/rejects from fleet execution reports, bounded-channel writer) |
| `model_decisions` | `strategist_bot` `src/Bot/Hosting/OrderLog.cs` `RecordDecision` (hooked 2026-11-16; reshaped the same day — see notes). Journal of strategy evaluations that emitted ≥ 1 order, from the Rust engine's `last_decision` snapshot (FFI ABI 6). `orders.DecisionId` is stamped by the drain. |
| `provision_jobs` | `strategist_bot` `src/Bot/Hosting/ProvisionJobMirror.cs` (hooked 2026-11-16; mirrors history fetch manifests from `HistoricalDataService.SaveManifest`) |
| `fleet_terminals` | `strategist_fleet` `Service/DbTerminalConfigStore.cs`, `Program.cs` |

QuestDB: `bars`, `ticks` — written/read by `strategist_bot`
(`Hosting/HistoricalDataService.cs`, `Hosting/FleetHistorySource.cs`,
`src/BacktestEngine/Api/QuestDbMarketData.cs`) via
`Infrastructure/TimeSeries` in this repo.

## Unused tables kept by decision (do NOT drop)

All 7 tables kept on 2026-11-16 are now hooked (see "Live tables" above);
none remain unused.

Notes on the hooked tables (realism gaps / limitations):

- `orders` rows now carry `RejectReason` (broker refusals via the adapters'
  `IRejectReasonSource` stash; pre-trade gate denials verbatim) and
  `RealizedPnl` (per-session average-cost attribution by `PositionLedger` —
  fills-the-log-saw only, price units × lots, not statement truth).
  `DecisionId` links to the `model_decisions` row that caused the order;
  null for manual/external orders and when the decision was evicted from
  the drain's bounded (venue, order id) map (cap 8192) or dropped by the
  channel's DropOldest policy.
- `model_decisions` was reshaped (`ReshapeModelDecisions` migration): the
  old bar-based quant-runtime columns (InstanceId/PackageId/BarTs/Step/
  InputJson/TargetsJson/Fired/LatencyUs) are gone; rows are now per
  order-emitting evaluation — account, venue, symbol, strategy descriptor
  (e.g. `onnx:models/x.onnx`), forecast (null in warmup/degraded), status
  (`warmup|active|degraded`), and a top-of-book + position `BookJson`
  snapshot. Deliberately NOT every evaluation (strategies evaluate per book
  update; that would be thousands of rows/sec). Forecast is the raw
  dimensionless signal (for classifiers, the argmax direction), and
  `BookJson` is top-of-book, not full depth.
- `backtest_results` rows written by the engine mirror have an empty
  `PackageId` (ad-hoc engine jobs are not package-driven).
- `strategy_packages` "validated" means structurally validated (readable
  zip + sane `manifest.json`); native-core validation is a documented gap.
- `provision_jobs` is a mirror of the on-disk manifests, which remain the
  source of truth for resume-after-restart; rows prune an hour after a
  job finishes. Manifests written before the mirror landed lack
  `ListingId` (deserializes as `Guid.Empty`).

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

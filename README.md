# strategist_shared

Shared artifacts of **Fleet** — the MT4/MT5 terminal fleet system
([ultimatesoftwares/Fleet](https://github.com/ultimatesoftwares/Fleet)).
This repo is the single source of truth for everything more than one Fleet
component consumes; vendor it as a git submodule (conventionally mounted at
`shared/`) and generate/import from there.

```sh
git submodule add https://github.com/ultimatesoftwares/strategist_shared.git shared
```

## Layout

| Directory | Contents |
|---|---|
| `Auth/` | `Strategist.Auth` — the JWT auth contract between the identity server (issuer) and resource services (validators): permission catalog, HS256 key-derivation convention (`SHA256(Jwt:SigningKey)`), and DI extensions (`AddStrategistJwtValidation`, `AddPermissionPolicies`). |
| `FleetBridge/` | The terminal-connector spike and the fleet FIX dialect [`spec/FIX44-FLEET.xml`](FleetBridge/spec/FIX44-FLEET.xml) — the single source of truth for the wire contract between the engine's acceptor, the MT4/MT5 terminal bridges, and the desktop feed clients; semantics in [`docs/fleet-fix.md`](docs/fleet-fix.md). |
| `Protos/` | gRPC contract of the backtest push feed (`backtestevents.BacktestFeed`, `Protos/backtest/v1`), hosted by strategist_server. |
| `Contracts/` | Transport-neutral wire shapes (`Strategist.Contracts.V1`), frozen by golden-payload characterization tests. |
| `Domain/` | Shared domain model definitions. |
| `EventFeed/` | `EventBroker` (subject-addressed latest-state cache + per-subscriber drop-oldest fan-out) and the backtest gRPC feed service. |
| `Trading.Core/` | The trading engine kernel shared across Fleet components. |
| `Infrastructure/` | PostgreSQL context factory, QuestDB time-series plane. |
| `Persistence/` | EF Core schema shared across Fleet services. |
| `OpenApi/` | REST contracts (`OpenApi/Bot`), re-exported on every build. |
| `LSE.NET/`, `MarketData.Lse/` | The London Strategic Edge data SDK. |

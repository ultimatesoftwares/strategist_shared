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
| [`protos/`](protos/README.md) | gRPC contract (`fleet.v1`): `TerminalService`, served by your engine, dialed by the EA connectors. Include root is `shared/protos` (imports are `fleet/v1/...`). |
| `database/` | Database schema/migrations shared across Fleet services. |
| `domain/` | Shared domain model definitions. |

# protos

gRPC contract of **Fleet** — the MT4/MT5 terminal fleet system
([ultimatesoftwares/Fleet](https://github.com/ultimatesoftwares/Fleet)).
Part of the `strategist_shared` repo — the single source of truth; consumers
vendor it (as the `strategist_shared` git submodule) and generate code locally.

## Files

| File | Package | Served by | Dialed by |
|---|---|---|---|
| `fleet/v1/common.proto` | `fleet.v1` | — (shared types) | — |
| `fleet/v1/terminal.proto` → `TerminalService` | `fleet.v1` | **your engine** | EA connectors (FleetBridge.dll inside each terminal) |

The direction matters: the engine hosts the `TerminalService` gRPC server
and every terminal connector dials *out* to it, holding one long-lived
bidirectional stream (`rpc Session(stream TerminalEvent) returns (stream
TerminalCommand)`). The connector introduces itself with `Hello`
(terminal id, platform, account), the engine answers with `welcome`, then
commands flow down and events (ticks, trade events, heartbeats, command
results) flow up. The full session contract is documented on
`TerminalService` in `fleet/v1/terminal.proto`.

`fleetd` (the Linux daemon in the main repo) provisions and supervises the
terminals and stamps the engine address + terminal id into each terminal
home; it does not speak this protocol itself.

## Include-root convention

The files import each other by their canonical names:

```proto
import "fleet/v1/common.proto";
```

so the **include root is this `protos/` directory** (it contains `fleet/`).
With the `strategist_shared` submodule mounted at `shared/`:

```sh
git submodule add https://github.com/ultimatesoftwares/strategist_shared.git shared

protoc -I shared/protos \
  shared/protos/fleet/v1/common.proto \
  shared/protos/fleet/v1/terminal.proto \
  --<lang>_out=...
```

### C# (Grpc.Tools)

```xml
<Protobuf Include="shared/protos/fleet/v1/common.proto" ProtoRoot="shared/protos" GrpcServices="None" />
<Protobuf Include="shared/protos/fleet/v1/terminal.proto" ProtoRoot="shared/protos" GrpcServices="Server" />
```

(`GrpcServices="Server"` for an engine — it hosts `TerminalService`.)

### Rust (tonic-build)

```rust
tonic_build::configure().compile_protos(
    &["shared/protos/fleet/v1/common.proto", "shared/protos/fleet/v1/terminal.proto"],
    &["shared/protos"],
)?;
```

### Go

`go_package` is pre-set to
`github.com/ultimatesoftwares/fleet/gen/fleet/v1;fleetv1`:

```sh
protoc -I shared/protos --go_out=gen --go-grpc_out=gen shared/protos/fleet/v1/*.proto
```

## Versioning

Everything lives in package `fleet.v1`. Changes are append-only within `v1`
(new fields/messages/RPCs); breaking changes get a new `v2` directory and
package.

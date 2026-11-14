# fleet-proto

gRPC contract of **Fleet** — the MT4/MT5 terminal fleet system
([ultimatesoftwares/fleet](https://github.com/ultimatesoftwares/fleet)). This
repo is the single source of truth; consumers vendor it (typically as a git
submodule) and generate code locally.

## Files

| File | Package | Served by | Dialed by |
|---|---|---|---|
| `v1/common.proto` | `fleet.v1` | — (shared types) | — |
| `v1/terminal.proto` → `TerminalService` | `fleet.v1` | **your engine** | EA connectors (FleetBridge.dll inside each terminal) |

The direction matters: the engine hosts the `TerminalService` gRPC server
and every terminal connector dials *out* to it, holding one long-lived
bidirectional stream (`rpc Session(stream TerminalEvent) returns (stream
TerminalCommand)`). The connector introduces itself with `Hello`
(terminal id, platform, account), the engine answers with `welcome`, then
commands flow down and events (ticks, trade events, heartbeats, command
results) flow up. The full session contract is documented on
`TerminalService` in `v1/terminal.proto`.

`fleetd` (the Linux daemon in the main repo) provisions and supervises the
terminals and stamps the engine address + terminal id into each terminal
home; it does not speak this protocol itself.

## Include-root convention

The files import each other by their canonical names:

```proto
import "fleet/v1/common.proto";
```

so the repo must sit inside a directory named `fleet`, and the **include root
is that directory's parent**. With a submodule mounted at `protos/fleet`:

```sh
git submodule add https://github.com/ultimatesoftwares/fleet-proto.git protos/fleet

protoc -I protos \
  protos/fleet/v1/common.proto \
  protos/fleet/v1/terminal.proto \
  --<lang>_out=...
```

### C# (Grpc.Tools)

```xml
<Protobuf Include="protos/fleet/v1/common.proto" ProtoRoot="protos" GrpcServices="None" />
<Protobuf Include="protos/fleet/v1/terminal.proto" ProtoRoot="protos" GrpcServices="Server" />
```

(`GrpcServices="Server"` for an engine — it hosts `TerminalService`.)

### Rust (tonic-build)

```rust
tonic_build::configure().compile_protos(
    &["protos/fleet/v1/common.proto", "protos/fleet/v1/terminal.proto"],
    &["protos"],
)?;
```

### Go

`go_package` is pre-set to
`github.com/ultimatesoftwares/fleet/gen/fleet/v1;fleetv1`:

```sh
protoc -I protos --go_out=gen --go-grpc_out=gen protos/fleet/v1/*.proto
```

## Versioning

Everything lives in package `fleet.v1`. Changes are append-only within `v1`
(new fields/messages/RPCs); breaking changes get a new `v2` directory and
package.

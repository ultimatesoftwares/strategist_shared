# fleet-proto

gRPC contract of the **Fleet hub** — the MT4/MT5 terminal fleet system
([ultimatesoftwares/fleet](https://github.com/ultimatesoftwares/fleet)). This
repo is the single source of truth; consumers vendor it (typically as a git
submodule) and generate code locally.

## Files

| File | Package | Served by | Dialed by |
|---|---|---|---|
| `v1/common.proto` | `fleet.v1` | — (shared types) | — |
| `v1/terminal.proto` → `TerminalService` | `fleet.v1` | the hub | EA connectors (FleetBridge.dll inside each terminal) |
| `v1/engine.proto` → `EngineGateway` | `fleet.v1` | **your engine** | the hub |

Note the direction of `EngineGateway`: the engine hosts the gRPC server and
the hub connects *out* to it (`rpc Link(stream HubMessage) returns (stream
EngineMessage)`). The hub introduces itself with `HubHello`, streams terminal
status snapshots and live events, and answers `ListTerminals`/`Execute`
requests correlated by `request_id`. An engine implements exactly one
service and treats every inbound `Link` as a hub coming online.

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
  protos/fleet/v1/engine.proto \
  --<lang>_out=...
```

### C# (Grpc.Tools)

```xml
<Protobuf Include="protos/fleet/v1/engine.proto" ProtoRoot="protos" GrpcServices="Server" />
```

(`GrpcServices="Server"` for an engine — it hosts `EngineGateway`.)

### Rust (tonic-build)

```rust
tonic_build::configure().compile_protos(
    &["protos/fleet/v1/common.proto", "protos/fleet/v1/engine.proto"],
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

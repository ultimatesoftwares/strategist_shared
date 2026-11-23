# FleetBridge (C# / NativeAOT)

C# port of the MT4/MT5 connector DLL the Fleet EAs load (succeeds the
retired C++ implementation — `bridge/` in strategist_server git history).
The EAs talk to this DLL through 34 undecorated `Fleet_*` exports (ABI v10);
the DLL talks FIX 4.4 (`spec/FIX44-FLEET.xml`, initiator, SenderCompID =
terminal id, TargetCompID = `STRATEGIST`) to the engine.

The FIX transport is fully wired: `Fleet_Init` starts a QuickFIX/n
`SocketInitiator` (stateless sessions — `ResetOnLogon=Y`, memory store; the
engine re-issues subscriptions after every logon), the Hello rides the Logon
(tags 20010–20017), the Welcome rides the Logon ack (20020–20022), commands
are flattened for `Fleet_PollCommand`/`Fleet_Cmd*`, and exactly one
CommandResult (U6) is emitted per non-empty command id (expired and
duplicate commands are auto-answered and never reach the EA).

## Layout

- `FleetBridge.csproj` + `Exports.cs`, `Bridge.cs`, `FleetApp.cs`,
  `SharedState.cs`, `Outbound.cs`, `Model.cs`, `Fields.cs`, `Consts.cs`,
  `Wstr.cs` — the DLL. Each file is a direct port of its C++ counterpart
  (`exports.cpp`, the `Bridge` struct, `fix_app.cpp`, `shared_state.cpp`,
  `outbound.cpp`, `model.h`, `fields.h`, `consts.h`, `wstr.cpp`).
  ABI_VERSION=10 must keep matching `MTF_ABI_VERSION` in the EAs — any bump
  requires recompiling the `.ex4`/`.ex5`.
- `ea/MQL4/Fleet.mq4`, `ea/MQL5/Fleet.mq5` — the Fleet EAs the terminals
  run; they live next to the DLL because EA and ABI must version together.
  The terminals Docker overlay compiles them with the base image's own
  MetaEditors.
- `spec/FIX44-FLEET.xml` — copy of the engine dialect (source of truth:
  strategist_bot `shared/Fix/FIX44-FLEET.xml`; never edit here). Embedded
  into the DLL and materialized to a temp file at `Fleet_Init` time.
- `tools/StateTests/` — JIT console harness: port of the C++
  `state_tests.cpp` (command queue, exactly-once results, builders, dedupe,
  deadline sweeping, outbound message shapes). Runs anywhere.
- `tools/AbiCheck/` — JIT console harness: loads a published DLL, asserts all
  34 exports resolve undecorated and the lifecycle contract holds.
- `tools/FixSmoke/` — AOT console: in-process acceptor+initiator logon with
  the FLEET dictionary. If it fails, first mitigations: `TrimmerRootAssembly`
  on the QuickFIX assembly, then `DynamicDependency` on reflected types.
- `winecheck/` — MinGW loader + `run.sh` to drive a CI-built DLL under Wine
  in the `terminals:latest` image.
- `../.github/workflows/fleetbridge.yml` — StateTests gate on Linux, then a
  windows-latest matrix (`win-x64` = MT5, `win-x86` = MT4; x86 AOT needs
  .NET 9+) running AbiCheck + FixSmoke, uploads `FleetBridge-<rid>`
  artifacts, and on main publishes both DLLs to GHCR
  (`ghcr.io/ultimatesoftwares/fleetbridge`) for the terminals Dockerfile.

## Building and testing

Windows only for the real DLLs — NativeAOT cannot cross-compile from Linux,
so local Linux dev gets `dotnet build` checking, the unit tests, and a
linux-x64 AOT publish that AbiCheck can load:

```sh
dotnet run -c Release --project FleetBridge/tools/StateTests/StateTests.csproj
dotnet publish FleetBridge/FleetBridge.csproj -c Release -r linux-x64
dotnet run -c Release --project FleetBridge/tools/AbiCheck/AbiCheck.csproj -- \
  FleetBridge/bin/Release/net10.0/linux-x64/publish/FleetBridge.so

# CI (windows-latest) produces the shippable DLLs:
dotnet publish FleetBridge/FleetBridge.csproj -c Release -r win-x64   # MT5
dotnet publish FleetBridge/FleetBridge.csproj -c Release -r win-x86   # MT4
```

Known-benign: ILC prints trim warnings from `QuickFix.DefaultMessageFactory`
(reflection over message factories); the FLEET dialect uses plain
`QuickFix.Message` throughout and FixSmoke proves logon works under AOT.

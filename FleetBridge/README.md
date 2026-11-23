# FleetBridge (C# / NativeAOT spike)

C# port of the MT4/MT5 connector DLL the Fleet EAs load (`bridge/` in
strategist_server is the proven C++ implementation this replaces). The EAs
talk to this DLL through 34 undecorated `Fleet_*` exports (ABI v10); the DLL
talks FIX 4.4 (`spec/FIX44-FLEET.xml`) to the engine.

## Status: spike — two feasibility gates before the C++ bridge is dropped

| Gate | What it proves | Where |
|------|----------------|-------|
| 1. Wine | A NativeAOT DLL (threads, GC, SEH) survives inside MT4/MT5 under Wine in the terminals image | `winecheck/` (run locally against CI artifacts) |
| 2. QuickFIX/n under AOT | QuickFIXn.Core 1.13.0 logs on when compiled with the bridge's AOT settings | `tools/FixSmoke/` (runs in CI) |

Until both pass, the FIX transport is **not wired up**: `Fleet_Init` /
`Fleet_Shutdown` / `Fleet_State` / `Fleet_LastError` / the log queue are real
(including a background worker thread to exercise the runtime under Wine),
and all data-path exports accept-and-drop. The terminals Docker image keeps
building the C++ DLLs in the meantime — deliberate.

## Layout

- `FleetBridge.csproj`, `Exports.cs`, `SharedState.cs`, `Consts.cs`, `Wstr.cs`
  — the DLL. `Exports.cs` is the C# equivalent of `bridge/bridge.def` +
  `bridge/src/exports.cpp`; `Consts.cs` mirrors `bridge/src/consts.h`
  (ABI_VERSION=10 must keep matching `MTF_ABI_VERSION` in the EAs — any bump
  requires recompiling the `.ex4`/`.ex5`).
- `spec/FIX44-FLEET.xml` — copy of the engine dialect (source of truth:
  strategist_bot `shared/Fix/FIX44-FLEET.xml`; never edit here).
- `tools/AbiCheck/` — JIT console harness: loads a published DLL, asserts all
  34 exports resolve undecorated and the lifecycle contract holds.
- `tools/FixSmoke/` — AOT console: in-process acceptor+initiator logon with
  the FLEET dictionary. If it fails, first mitigations: `TrimmerRootAssembly`
  on the QuickFIX assembly, then `DynamicDependency` on reflected types.
- `winecheck/` — MinGW loader + `run.sh` to drive a CI-built DLL under Wine
  in the `terminals:latest` image.
- `../.github/workflows/fleetbridge.yml` — windows-latest matrix
  (`win-x64` = MT5, `win-x86` = MT4; x86 AOT needs .NET 9+), runs both gates,
  uploads `FleetBridge-<rid>` artifacts.

## Building

Windows only — NativeAOT cannot cross-compile from Linux, so local Linux dev
gets compile/`dotnet build` checking only and CI produces the real DLLs:

```sh
dotnet publish FleetBridge/FleetBridge.csproj -c Release -r win-x64
dotnet publish FleetBridge/FleetBridge.csproj -c Release -r win-x86
```

## After both gates pass (planned)

1. Port the FIX session + state machine from `bridge/src/` into
   `SharedState`/a `FixTransport`, reusing QuickFIXn.Core.
2. Switch the terminals Dockerfile to pull `FleetBridge.dll` from CI instead
   of compiling the C++ bridge (recommendation: publish to GHCR as an OCI
   image or attach to Releases — raw workflow artifacts expire and need auth).
3. Delete `bridge/` from strategist_server.

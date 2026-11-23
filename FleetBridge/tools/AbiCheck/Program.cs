// ABI gate for FleetBridge.dll (see AbiCheck.csproj for usage).
// Exit code 0 = all checks passed; non-zero = failure (message on stderr).

using System.Runtime.InteropServices;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: AbiCheck <path-to-FleetBridge.dll>");
    return 2;
}

string dllPath = Path.GetFullPath(args[0]);
if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"FAIL: DLL not found: {dllPath}");
    return 2;
}

// Must match bridge.def / the EA #import blocks exactly.
string[] expectedExports =
[
    "Fleet_Version", "Fleet_Init", "Fleet_Shutdown", "Fleet_State",
    "Fleet_LastError", "Fleet_HeartbeatIntervalMs", "Fleet_Log",
    "Fleet_NextLog", "Fleet_PushTick", "Fleet_BeginHeartbeat",
    "Fleet_EndHeartbeat", "Fleet_BeginDepth", "Fleet_AddDepthRow",
    "Fleet_EndDepth", "Fleet_PushPositionEvent", "Fleet_PushOrderEvent",
    "Fleet_PollCommand", "Fleet_CmdInt", "Fleet_CmdDouble", "Fleet_CmdString",
    "Fleet_SendAck", "Fleet_SendError", "Fleet_SendOrderResult",
    "Fleet_SendAccountInfo", "Fleet_BeginResult", "Fleet_AddBar",
    "Fleet_AddPosition", "Fleet_AddOrder", "Fleet_AddSymbolInfo",
    "Fleet_AddCloseResult", "Fleet_AddDeal", "Fleet_AddDealV2",
    "Fleet_AddDealV3", "Fleet_EndResult",
];

int failures = 0;

IntPtr lib;
try
{
    lib = NativeLibrary.Load(dllPath);
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAIL: could not load {dllPath}: {e.Message}");
    return 1;
}

Console.WriteLine($"loaded {dllPath}");

// 1. Every export must resolve by its undecorated name.
var found = new Dictionary<string, IntPtr>();
foreach (string name in expectedExports)
{
    if (NativeLibrary.TryGetExport(lib, name, out IntPtr addr))
    {
        found[name] = addr;
    }
    else
    {
        Console.Error.WriteLine($"FAIL: missing export {name}");
        failures++;
    }
}
Console.WriteLine($"exports resolved: {found.Count}/{expectedExports.Length}");

if (failures == 0)
{
    unsafe
    {
        // 2. ABI canary.
        var version = (delegate* unmanaged[Stdcall]<int>)found["Fleet_Version"];
        int abi = version();
        Check(abi == 10, $"Fleet_Version == 10 (got {abi})");

        // 3. Bad init (null args) must fail cleanly with -1 ...
        var init = (delegate* unmanaged[Stdcall]<
            ushort*, ushort*, int, ushort*, long, ushort*, int, int, int,
            ushort*, int>)found["Fleet_Init"];
        int rc = init(null, null, 0, null, 0, null, 0, 0, 0, null);
        Check(rc == -1, $"Fleet_Init(null...) == -1 (got {rc})");

        // 4. ... and leave a non-empty diagnostic in Fleet_LastError.
        var lastError = (delegate* unmanaged[Stdcall]<ushort*, int, int>)
            found["Fleet_LastError"];
        ushort* buf = stackalloc ushort[256];
        int written = lastError(buf, 256);
        string msg = written > 0 ? new string((char*)buf, 0, written) : "";
        Check(written > 0, $"Fleet_LastError non-empty (got \"{msg}\")");

        // 5. Good init / state / shutdown round-trip.
        fixed (char* engine = "127.0.0.1:19876")
        fixed (char* tid = "abicheck-terminal")
        fixed (char* build = "0")
        fixed (char* broker = "AbiCheck-Server")
        fixed (char* brokerName = "AbiCheck")
        {
            rc = init((ushort*)engine, (ushort*)tid, 5, (ushort*)build, 1,
                      (ushort*)broker, 0, 0, 0, (ushort*)brokerName);
            Check(rc == 0, $"Fleet_Init(valid) == 0 (got {rc})");
        }

        var state = (delegate* unmanaged[Stdcall]<int>)found["Fleet_State"];
        int st = state();
        Check(st is 1 or 2, $"Fleet_State in {{CONNECTING, UP}} (got {st})");

        var shutdown = (delegate* unmanaged[Stdcall]<int>)found["Fleet_Shutdown"];
        rc = shutdown();
        Check(rc == 0, $"Fleet_Shutdown == 0 (got {rc})");
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"ABI CHECK FAILED: {failures} failure(s)");
    return 1;
}
Console.WriteLine("ABI CHECK PASSED");
return 0;

void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok)
    {
        failures++;
    }
}

/*
 * Minimal Win32 loader for the Wine feasibility gate: loads FleetBridge.dll
 * the same way MT4/MT5 would (LoadLibraryW + GetProcAddress) and drives the
 * lifecycle exports. Run it under the terminals Docker image's Wine prefix
 * via run.sh.
 *
 * Cross-compile on Linux:
 *   x86_64-w64-mingw32-gcc-posix -O2 -o winecheck64.exe loader.c  # MT5 DLL
 *   i686-w64-mingw32-gcc-posix   -O2 -o winecheck32.exe loader.c  # MT4 DLL
 *
 * Exit code 0 = all checks passed.
 */
#include <stdio.h>
#include <stdint.h>
#include <windows.h>

typedef int32_t(__stdcall *fn_version)(void);
typedef int32_t(__stdcall *fn_init)(const wchar_t *, const wchar_t *, int32_t,
                                    const wchar_t *, int64_t, const wchar_t *,
                                    int32_t, int32_t, int32_t,
                                    const wchar_t *);
typedef int32_t(__stdcall *fn_state)(void);
typedef int32_t(__stdcall *fn_last_error)(wchar_t *, int32_t);
typedef int32_t(__stdcall *fn_next_log)(wchar_t *, int32_t);
typedef int32_t(__stdcall *fn_shutdown)(void);

static int g_failures = 0;

static void check(int ok, const char *what) {
    printf("%s %s\n", ok ? "ok  " : "FAIL", what);
    if (!ok) {
        g_failures++;
    }
}

int wmain(int argc, wchar_t **argv) {
    if (argc != 2) {
        fwprintf(stderr, L"usage: winecheck <path-to-FleetBridge.dll>\n");
        return 2;
    }

    HMODULE lib = LoadLibraryW(argv[1]);
    if (!lib) {
        fwprintf(stderr, L"FAIL: LoadLibraryW(%ls) error %lu\n", argv[1],
                 GetLastError());
        return 1;
    }
    wprintf(L"loaded %ls\n", argv[1]);

    static const char *names[] = {
        "Fleet_Version",       "Fleet_Init",
        "Fleet_Shutdown",      "Fleet_State",
        "Fleet_LastError",     "Fleet_HeartbeatIntervalMs",
        "Fleet_Log",           "Fleet_NextLog",
        "Fleet_PushTick",      "Fleet_BeginHeartbeat",
        "Fleet_EndHeartbeat",  "Fleet_BeginDepth",
        "Fleet_AddDepthRow",   "Fleet_EndDepth",
        "Fleet_PushPositionEvent", "Fleet_PushOrderEvent",
        "Fleet_PollCommand",   "Fleet_CmdInt",
        "Fleet_CmdDouble",     "Fleet_CmdString",
        "Fleet_SendAck",       "Fleet_SendError",
        "Fleet_SendOrderResult", "Fleet_SendAccountInfo",
        "Fleet_BeginResult",   "Fleet_AddBar",
        "Fleet_AddPosition",   "Fleet_AddOrder",
        "Fleet_AddSymbolInfo", "Fleet_AddCloseResult",
        "Fleet_AddDeal",       "Fleet_AddDealV2",
        "Fleet_AddDealV3",     "Fleet_EndResult",
    };
    const int expected = (int)(sizeof(names) / sizeof(names[0]));
    int resolved = 0;
    for (int i = 0; i < expected; i++) {
        if (GetProcAddress(lib, names[i])) {
            resolved++;
        } else {
            printf("FAIL missing export %s\n", names[i]);
            g_failures++;
        }
    }
    printf("exports resolved: %d/%d\n", resolved, expected);

    fn_version version = (fn_version)GetProcAddress(lib, "Fleet_Version");
    fn_init init = (fn_init)GetProcAddress(lib, "Fleet_Init");
    fn_state state = (fn_state)GetProcAddress(lib, "Fleet_State");
    fn_last_error last_error =
        (fn_last_error)GetProcAddress(lib, "Fleet_LastError");
    fn_next_log next_log = (fn_next_log)GetProcAddress(lib, "Fleet_NextLog");
    fn_shutdown shutdown = (fn_shutdown)GetProcAddress(lib, "Fleet_Shutdown");
    if (!version || !init || !state || !last_error || !next_log || !shutdown) {
        fprintf(stderr, "FAIL: lifecycle exports missing; aborting\n");
        return 1;
    }

    check(version() == 10, "Fleet_Version == 10");

    check(init(NULL, NULL, 0, NULL, 0, NULL, 0, 0, 0, NULL) == -1,
          "Fleet_Init(null...) == -1");
    wchar_t err[256];
    check(last_error(err, 256) > 0, "Fleet_LastError non-empty");

    int rc = init(L"127.0.0.1:19876", L"winecheck-terminal", 5, L"0", 1,
                  L"WineCheck-Server", 0, 0, 0, L"WineCheck");
    check(rc == 0, "Fleet_Init(valid) == 0");
    int st = state();
    check(st == 1 || st == 2, "Fleet_State in {CONNECTING, UP}");

    /* Let the managed worker thread run a few GC-exercising sweeps. */
    Sleep(12000);
    wchar_t line[512];
    int got_log = 0;
    while (next_log(line, 512) > 0) {
        wprintf(L"log: %ls\n", line);
        got_log = 1;
    }
    check(got_log, "worker thread produced log lines (threads+GC alive)");

    check(shutdown() == 0, "Fleet_Shutdown == 0");
    check(state() == 0, "Fleet_State == DOWN after shutdown");

    if (g_failures > 0) {
        fprintf(stderr, "WINE CHECK FAILED: %d failure(s)\n", g_failures);
        return 1;
    }
    printf("WINE CHECK PASSED\n");
    return 0;
}

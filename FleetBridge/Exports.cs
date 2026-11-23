using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FleetBridge;

/// <summary>
/// The 34 Fleet_* exports the MQL4/MQL5 EAs import. Entry-point names must
/// stay undecorated (MQL4 #import cannot resolve _Fleet_X@N); declaring
/// stdcall here is required for win-x86 and harmless on win-x64, and
/// UnmanagedCallersOnly(EntryPoint=...) keeps the names undecorated either
/// way — the C# equivalent of bridge.def.
///
/// No exception may ever escape into MT4/MT5: every body is wrapped.
/// </summary>
internal static unsafe class Exports
{
    /// <summary>Accepts a data-path call: 0 when the bridge is up, -1 otherwise.</summary>
    private static int Accept() => SharedState.WithShared(-1, static _ => 0);

    // ---- lifecycle ----------------------------------------------------------

    /// <summary>Load canary: the EA verifies the ABI before calling anything else.</summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_Version", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Version() => Consts.AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "Fleet_Init", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Init(
        ushort* engineAddress, ushort* terminalId, int platform,
        ushort* terminalBuild, long accountLogin, ushort* brokerServer,
        int serverTzOffsetMinutes, int useTls, int marginMode,
        ushort* brokerName)
    {
        try
        {
            string engine = Wstr.FromUtf16(engineAddress);
            string tid = Wstr.FromUtf16(terminalId);
            if (engine.Length == 0 || tid.Length == 0)
            {
                SharedState.SetGlobalError("engine address and terminal id are required");
                return -1;
            }
            if (useTls != 0)
            {
                SharedState.SetGlobalError(
                    "TLS requested but this DLL was built without TLS support; " +
                    "terminate TLS in front of the engine or rebuild with it");
                return -1;
            }
            // The EA used to pass a gRPC URL; accept and strip a scheme prefix.
            foreach (string scheme in new[] { "http://", "https://", "fix://" })
            {
                if (engine.StartsWith(scheme, StringComparison.Ordinal))
                {
                    engine = engine[scheme.Length..];
                    break;
                }
            }
            engine = engine.TrimEnd('/');
            int colon = engine.LastIndexOf(':');
            if (colon < 0 || colon + 1 >= engine.Length)
            {
                SharedState.SetGlobalError($"engine address must be host:port, got '{engine}'");
                return -1;
            }
            string host = engine[..colon];
            if (host.Length == 0 ||
                !int.TryParse(engine.AsSpan(colon + 1), out int port) ||
                port <= 0 || port > 65535)
            {
                SharedState.SetGlobalError($"engine address must be host:port, got '{engine}'");
                return -1;
            }

            SharedState.Start(new BridgeConfig
            {
                Host = host,
                Port = port,
                TerminalId = tid,
                Platform = platform,
                TerminalBuild = Wstr.FromUtf16(terminalBuild),
                AccountLogin = accountLogin,
                BrokerServer = Wstr.FromUtf16(brokerServer),
                ServerTzOffsetMinutes = serverTzOffsetMinutes,
                MarginMode = marginMode,
                BrokerName = Wstr.FromUtf16(brokerName),
            });
            return 0;
        }
        catch (Exception e)
        {
            SharedState.SetGlobalError("exception during Fleet_Init: " + e.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_Shutdown", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Shutdown()
    {
        try
        {
            return SharedState.Shutdown();
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_State", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int State()
    {
        try
        {
            return SharedState.WithShared(Consts.StateDown, static s => s.State);
        }
        catch
        {
            return Consts.StateDown;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_LastError", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int LastError(ushort* buffer, int capacity)
    {
        try
        {
            string msg = SharedState.WithShared("", static s => s.LastError);
            if (msg.Length == 0)
            {
                msg = SharedState.GlobalError();
            }
            return Wstr.WriteUtf16(msg, buffer, capacity);
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_HeartbeatIntervalMs", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int HeartbeatIntervalMs()
    {
        try
        {
            return SharedState.WithShared((int)Consts.DefaultHeartbeatMs,
                static s => (int)s.HeartbeatIntervalMs);
        }
        catch
        {
            return (int)Consts.DefaultHeartbeatMs;
        }
    }

    // ---- logging ------------------------------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_Log", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Log(int level, ushort* message)
    {
        try
        {
            string msg = Wstr.FromUtf16(message);
            return SharedState.WithShared(-1, s =>
            {
                // Spike: queue locally until the FIX LogEvent path is wired up.
                s.PushLog($"[ea:{level}] {msg}");
                return 0;
            });
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_NextLog", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int NextLog(ushort* buffer, int capacity)
    {
        try
        {
            string? line = SharedState.WithShared<string?>(null, static s => s.NextLog());
            return line is null ? 0 : Wstr.WriteUtf16(line, buffer, capacity);
        }
        catch
        {
            return 0;
        }
    }

    // ---- market data / account push (stubs until FIX transport lands) -------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushTick", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushTick(
        ushort* symbol, double bid, double ask, double last, long volume,
        long timeMs, int spreadPoints, double balance, double equity,
        double margin, double marginFree)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginHeartbeat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginHeartbeat(int terminalConnected, double equity, double balance, int pendingOrders)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndHeartbeat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndHeartbeat()
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginDepth", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginDepth(ushort* symbol, long timeMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDepthRow", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDepthRow(int side, double price, double volume)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndDepth", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndDepth()
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushPositionEvent", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushPositionEvent(
        int kind, long ticket, ushort* symbol, int orderType, double volume,
        double priceOpen, double priceCurrent, double stopLoss,
        double takeProfit, double profit, double swap, double commission,
        ushort* comment, long magic, long openTimeMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushOrderEvent", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushOrderEvent(
        int kind, long ticket, ushort* symbol, int orderType,
        double volumeInitial, double volumeCurrent, double price,
        double priceCurrent, double stopLimitPrice, double stopLoss,
        double takeProfit, ushort* comment, long magic, long setupTimeMs,
        long expirationMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    // ---- command polling (stubs: no inbound commands until FIX lands) -------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PollCommand", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PollCommand(ushort* idBuffer, int idCapacity)
    {
        return 0; // no pending command
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdInt", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static long CmdInt(ushort* name) => 0;

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdDouble", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static double CmdDouble(ushort* name) => 0.0;

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdString", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int CmdString(ushort* name, ushort* buffer, int capacity)
    {
        try
        {
            return Wstr.WriteUtf16("", buffer, capacity);
        }
        catch
        {
            return 0;
        }
    }

    // ---- command replies (stubs) ---------------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendAck", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendAck(ushort* commandId)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendError", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendError(ushort* commandId, int code, int platformCode, ushort* message, int retryable)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendOrderResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendOrderResult(
        ushort* commandId, long orderTicket, long positionTicket,
        double executedPrice, double executedVolume, ushort* clientOrderId)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendAccountInfo", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendAccountInfo(
        ushort* commandId, long login, ushort* server, ushort* company,
        ushort* currency, double balance, double equity, double margin,
        double marginFree, double marginLevel, int leverage, int tradeAllowed,
        ushort* accountName,
        // ACCOUNT_TRADE_MODE: 0 = demo, 1 = contest, 2 = real.
        int tradeMode)
    {
        try { return Accept(); } catch { return -1; }
    }

    // ---- result builder (stubs) ----------------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginResult(ushort* commandId)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndResult(int payloadType)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddBar", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddBar(
        long timeMs, double open, double high, double low, double close,
        long tickVolume, long realVolume, int spread)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddPosition", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddPosition(
        long ticket, ushort* symbol, int orderType, double volume,
        double priceOpen, double priceCurrent, double stopLoss,
        double takeProfit, double profit, double swap, double commission,
        ushort* comment, long magic, long openTimeMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddOrder", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddOrder(
        long ticket, ushort* symbol, int orderType, double volumeInitial,
        double volumeCurrent, double price, double priceCurrent,
        double stopLimitPrice, double stopLoss, double takeProfit,
        ushort* comment, long magic, long setupTimeMs, long expirationMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddSymbolInfo", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddSymbolInfo(
        ushort* symbol, ushort* description, int digits, double point,
        double tickSize, double tickValue, double contractSize,
        double volumeMin, double volumeMax, double volumeStep,
        int stopsLevelPoints, int freezeLevelPoints, double marginRequired,
        ushort* currencyBase, ushort* currencyProfit, ushort* currencyMargin,
        int tradeAllowed, long spreadPoints)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddCloseResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddCloseResult(
        long ticket, int ok, double closedVolume, double closePrice,
        int errorCode, int platformCode, ushort* message, int retryable)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDeal", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDeal(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDealV2", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDealV2(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs, int entry)
    {
        try { return Accept(); } catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDealV3", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDealV3(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs, int entry, double fee)
    {
        try { return Accept(); } catch { return -1; }
    }
}

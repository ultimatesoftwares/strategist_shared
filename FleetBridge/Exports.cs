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
/// Safety contract shared by every export: ushort* arguments must be
/// NUL-terminated UTF-16 strings (null is treated as empty) and buffers must
/// be writable for `capacity` elements — exactly what MQL guarantees when
/// passing `string` and `ushort buf[]` to DLL imports. No exception ever
/// escapes an export. Port of bridge/src/exports.cpp.
/// </summary>
internal static unsafe class Exports
{
    private static readonly object BridgeMu = new();
    private static Bridge? _bridge;
    private static readonly object ErrorMu = new();
    private static string _globalError = "";

    private static void SetGlobalError(string msg)
    {
        lock (ErrorMu)
        {
            _globalError = msg;
        }
    }

    private static string GlobalError()
    {
        lock (ErrorMu)
        {
            return _globalError;
        }
    }

    /// <summary>
    /// Runs <paramref name="fn"/> against the live bridge, or returns
    /// <paramref name="def"/> when Init has not succeeded yet. Never lets an
    /// exception cross the FFI boundary.
    /// </summary>
    private static T WithShared<T>(T def, Func<SharedState, T> fn)
    {
        try
        {
            lock (BridgeMu)
            {
                return _bridge is null ? def : fn(_bridge.Shared);
            }
        }
        catch
        {
            SetGlobalError("exception inside FleetBridge; see terminal logs");
            return def;
        }
    }

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
                SetGlobalError("engine address and terminal id are required");
                return -1;
            }
            if (useTls != 0)
            {
                SetGlobalError(
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
                SetGlobalError($"engine address must be host:port, got '{engine}'");
                return -1;
            }
            string host = engine[..colon];
            if (host.Length == 0 ||
                !int.TryParse(engine.AsSpan(colon + 1), out int port) ||
                port <= 0 || port > 65535)
            {
                SetGlobalError($"engine address must be host:port, got '{engine}'");
                return -1;
            }

            var cfg = new BridgeConfig
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
            };

            lock (BridgeMu)
            {
                // EA re-attach: restart cleanly with the new config.
                _bridge?.Stop();
                _bridge = null;

                var bridge = new Bridge();
                try
                {
                    bridge.Start(cfg);
                }
                catch (Exception e)
                {
                    bridge.Stop();
                    SetGlobalError("failed to start FIX session: " + e.Message);
                    return -1;
                }
                SetGlobalError("");
                _bridge = bridge;
                return 0;
            }
        }
        catch (Exception e)
        {
            SetGlobalError("exception during Fleet_Init: " + e.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_Shutdown", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Shutdown()
    {
        try
        {
            lock (BridgeMu)
            {
                _bridge?.Stop();
                _bridge = null;
            }
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>0 = disconnected, 1 = connecting, 2 = session up.</summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_State", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int State()
    {
        try
        {
            return WithShared(Consts.StateDown, static s => s.State);
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
            string msg = WithShared("", static s => s.LastError);
            if (msg.Length == 0)
            {
                msg = GlobalError();
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
            return WithShared((int)Consts.DefaultHeartbeatMs,
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
            return WithShared(-1, s =>
            {
                s.Send(Outbound.LogEvent(level, msg, Consts.NowMs()));
                return 0;
            });
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Drains one line from the bridge's internal event log (connects,
    /// backoff, commands, results, drops). The EA polls this every timer
    /// tick and prints to the Experts tab. Returns the number of UTF-16
    /// units written, 0 = empty.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_NextLog", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int NextLog(ushort* buffer, int capacity)
    {
        try
        {
            string? line = WithShared<string?>(null, static s => s.NextLog());
            return line is null ? 0 : Wstr.WriteUtf16(line, buffer, capacity);
        }
        catch
        {
            return 0;
        }
    }

    // ---- outbound events (EA -> engine) --------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushTick", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushTick(
        ushort* symbol, double bid, double ask, double last, long volume,
        long timeMs, int spreadPoints, double balance, double equity,
        double margin, double marginFree)
    {
        try
        {
            var tick = new Tick
            {
                Symbol = Wstr.FromUtf16(symbol),
                Bid = bid,
                Ask = ask,
                Last = last,
                Volume = volume > 0 ? (ulong)volume : 0,
                TimeUnixMs = timeMs,
                SpreadPoints = spreadPoints > 0 ? spreadPoints : 0,
            };
            var account = new AccountSnapshot
            {
                Balance = balance,
                Equity = equity,
                Margin = margin,
                MarginFree = marginFree,
            };
            return WithShared(-1, s =>
            {
                s.PushTick(tick, account);
                return 0;
            });
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Starts a heartbeat snapshot. The EA then appends one Fleet_AddPosition
    /// per open position and seals the message with Fleet_EndHeartbeat, which
    /// stamps the time and ships it.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginHeartbeat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginHeartbeat(int terminalConnected, double equity, double balance, int pendingOrders)
    {
        try
        {
            return WithShared(-1, s =>
                s.BeginHeartbeat(terminalConnected != 0, equity, balance, pendingOrders));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndHeartbeat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndHeartbeat()
    {
        try
        {
            return WithShared(-1, static s => s.EndHeartbeat());
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Starts a market-depth (DOM) snapshot for `symbol`. The EA appends one
    /// Fleet_AddDepthRow per book level (in the terminal's own order) and
    /// seals it with Fleet_EndDepth, which ships it immediately —
    /// zero-latency, like ticks. `timeMs` 0 lets the bridge stamp the send
    /// time itself.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginDepth", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginDepth(ushort* symbol, long timeMs)
    {
        try
        {
            string sym = Wstr.FromUtf16(symbol);
            return WithShared(-1, s => s.BeginDepth(sym, timeMs));
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Appends one book level: side 1 = sell/ask, 2 = buy/bid.</summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDepthRow", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDepthRow(int side, double price, double volume)
    {
        try
        {
            return WithShared(-1, s => s.AddDepthRow(side, price, volume));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndDepth", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndDepth()
    {
        try
        {
            return WithShared(-1, static s => s.EndDepth());
        }
        catch
        {
            return -1;
        }
    }

    // ---- trade events (EA -> engine) -----------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushPositionEvent", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushPositionEvent(
        int kind, long ticket, ushort* symbol, int orderType, double volume,
        double priceOpen, double priceCurrent, double stopLoss,
        double takeProfit, double profit, double swap, double commission,
        ushort* comment, long magic, long openTimeMs)
    {
        try
        {
            Position p = MakePosition(
                ticket, symbol, orderType, volume, priceOpen, priceCurrent,
                stopLoss, takeProfit, profit, swap, commission, comment,
                magic, openTimeMs);
            return WithShared(-1, s =>
            {
                s.Send(Outbound.TradeEventPosition(kind, Consts.NowMs(), p));
                return 0;
            });
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_PushOrderEvent", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PushOrderEvent(
        int kind, long ticket, ushort* symbol, int orderType,
        double volumeInitial, double volumeCurrent, double price,
        double priceCurrent, double stopLimitPrice, double stopLoss,
        double takeProfit, ushort* comment, long magic, long setupTimeMs,
        long expirationMs)
    {
        try
        {
            PendingOrder o = MakeOrder(
                ticket, symbol, orderType, volumeInitial, volumeCurrent,
                price, priceCurrent, stopLimitPrice, stopLoss, takeProfit,
                comment, magic, setupTimeMs, expirationMs);
            return WithShared(-1, s =>
            {
                s.Send(Outbound.TradeEventOrder(kind, Consts.NowMs(), o));
                return 0;
            });
        }
        catch
        {
            return -1;
        }
    }

    // ---- command polling (engine -> EA) --------------------------------------

    /// <summary>
    /// Returns the next command's type id (0 = none) and writes its
    /// command_id into the buffer. Expired/duplicate commands are answered
    /// internally and never surface here.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_PollCommand", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int PollCommand(ushort* idBuffer, int idCapacity)
    {
        try
        {
            (int type, string id) = WithShared((0, ""), static s => s.Poll());
            if (type != 0)
            {
                Wstr.WriteUtf16(id, idBuffer, idCapacity);
            }
            return type;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdInt", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static long CmdInt(ushort* name)
    {
        try
        {
            string n = Wstr.FromUtf16(name);
            return WithShared(0L, s => s.CmdI64(n));
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdDouble", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static double CmdDouble(ushort* name)
    {
        try
        {
            string n = Wstr.FromUtf16(name);
            return WithShared(0.0, s => s.CmdF64(n));
        }
        catch
        {
            return 0.0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_CmdString", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int CmdString(ushort* name, ushort* buffer, int capacity)
    {
        try
        {
            string n = Wstr.FromUtf16(name);
            string value = WithShared("", s => s.CmdString(n));
            return Wstr.WriteUtf16(value, buffer, capacity);
        }
        catch
        {
            return 0;
        }
    }

    // ---- command results (EA -> engine) --------------------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendAck", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendAck(ushort* commandId)
    {
        try
        {
            string id = Wstr.FromUtf16(commandId);
            return WithShared(-1, s => s.Complete(id, Outbound.AckResult(id)));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendError", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendError(ushort* commandId, int code, int platformCode, ushort* message, int retryable)
    {
        try
        {
            string id = Wstr.FromUtf16(commandId);
            string msg = Wstr.FromUtf16(message);
            return WithShared(-1, s => s.Complete(
                id, Outbound.ErrorResult(id, code, platformCode, msg, retryable != 0)));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendOrderResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendOrderResult(
        ushort* commandId, long orderTicket, long positionTicket,
        double executedPrice, double executedVolume, ushort* clientOrderId)
    {
        try
        {
            string id = Wstr.FromUtf16(commandId);
            var r = new OrderResult
            {
                OrderTicket = orderTicket > 0 ? (ulong)orderTicket : 0,
                PositionTicket = positionTicket > 0 ? (ulong)positionTicket : 0,
                ExecutedPrice = executedPrice,
                ExecutedVolume = executedVolume,
                ClientOrderId = Wstr.FromUtf16(clientOrderId),
            };
            return WithShared(-1, s => s.Complete(id, Outbound.OrderResultMsg(id, r)));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_SendAccountInfo", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SendAccountInfo(
        ushort* commandId, long login, ushort* server, ushort* company,
        ushort* currency, double balance, double equity, double margin,
        double marginFree, double marginLevel, int leverage, int tradeAllowed,
        ushort* accountName,
        // ACCOUNT_TRADE_MODE: 0 = demo, 1 = contest, 2 = real (MT4 EAs send
        // IsDemo() ? 0 : 2). Any other value maps to "" (unknown).
        int tradeMode)
    {
        try
        {
            string id = Wstr.FromUtf16(commandId);
            var info = new AccountInfo
            {
                Login = login,
                BrokerServer = Wstr.FromUtf16(server),
                BrokerCompany = Wstr.FromUtf16(company),
                Currency = Wstr.FromUtf16(currency),
                Balance = balance,
                Equity = equity,
                Margin = margin,
                MarginFree = marginFree,
                MarginLevel = marginLevel,
                Leverage = leverage,
                TradeAllowed = tradeAllowed != 0,
                AccountName = Wstr.FromUtf16(accountName),
                Environment = tradeMode switch
                {
                    0 => "demo",
                    1 => "contest",
                    2 => "live",
                    _ => "",
                },
            };
            return WithShared(-1, s => s.Complete(id, Outbound.AccountInfoResult(id, info)));
        }
        catch
        {
            return -1;
        }
    }

    // ---- multi-row result building (EA -> engine) ----------------------------

    [UnmanagedCallersOnly(EntryPoint = "Fleet_BeginResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BeginResult(ushort* commandId)
    {
        try
        {
            string id = Wstr.FromUtf16(commandId);
            return WithShared(-1, s => s.BeginResult(id));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_EndResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int EndResult(int payloadType)
    {
        try
        {
            return WithShared(-1, s => s.EndResult(payloadType));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddBar", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddBar(
        long timeMs, double open, double high, double low, double close,
        long tickVolume, long realVolume, int spread)
    {
        try
        {
            var bar = new Bar
            {
                TimeUnixMs = timeMs,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                TickVolume = tickVolume > 0 ? (ulong)tickVolume : 0,
                RealVolume = realVolume > 0 ? (ulong)realVolume : 0,
                Spread = spread,
            };
            return WithShared(-1, s => s.WithBuilder(b =>
            {
                b.Bars.Add(bar);
                return b.Bars.Count;
            }));
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Appends a position row to the active snapshot: a heartbeat under
    /// construction (Fleet_BeginHeartbeat) takes precedence, otherwise the
    /// multi-row command result (Fleet_BeginResult).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddPosition", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddPosition(
        long ticket, ushort* symbol, int orderType, double volume,
        double priceOpen, double priceCurrent, double stopLoss,
        double takeProfit, double profit, double swap, double commission,
        ushort* comment, long magic, long openTimeMs)
    {
        try
        {
            Position p = MakePosition(
                ticket, symbol, orderType, volume, priceOpen, priceCurrent,
                stopLoss, takeProfit, profit, swap, commission, comment,
                magic, openTimeMs);
            return WithShared(-1, s => s.AddPosition(p));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddOrder", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddOrder(
        long ticket, ushort* symbol, int orderType, double volumeInitial,
        double volumeCurrent, double price, double priceCurrent,
        double stopLimitPrice, double stopLoss, double takeProfit,
        ushort* comment, long magic, long setupTimeMs, long expirationMs)
    {
        try
        {
            PendingOrder o = MakeOrder(
                ticket, symbol, orderType, volumeInitial, volumeCurrent,
                price, priceCurrent, stopLimitPrice, stopLoss, takeProfit,
                comment, magic, setupTimeMs, expirationMs);
            return WithShared(-1, s => s.WithBuilder(b =>
            {
                b.Orders.Add(o);
                return b.Orders.Count;
            }));
        }
        catch
        {
            return -1;
        }
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
        try
        {
            var info = new SymbolInfo
            {
                Symbol = Wstr.FromUtf16(symbol),
                Description = Wstr.FromUtf16(description),
                Digits = digits,
                Point = point,
                TickSize = tickSize,
                TickValue = tickValue,
                ContractSize = contractSize,
                VolumeMin = volumeMin,
                VolumeMax = volumeMax,
                VolumeStep = volumeStep,
                StopsLevelPoints = stopsLevelPoints,
                FreezeLevelPoints = freezeLevelPoints,
                MarginRequired = marginRequired,
                CurrencyBase = Wstr.FromUtf16(currencyBase),
                CurrencyProfit = Wstr.FromUtf16(currencyProfit),
                CurrencyMargin = Wstr.FromUtf16(currencyMargin),
                TradeAllowed = tradeAllowed != 0,
                SpreadPoints = spreadPoints,
            };
            return WithShared(-1, s => s.WithBuilder(b =>
            {
                b.Symbols.Add(info);
                return b.Symbols.Count;
            }));
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Appends one per-ticket outcome to a ClosePositionsBatch result.
    /// errorCode/platformCode/message/retryable are only read when ok == 0.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddCloseResult", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddCloseResult(
        long ticket, int ok, double closedVolume, double closePrice,
        int errorCode, int platformCode, ushort* message, int retryable)
    {
        try
        {
            var r = new CloseResult
            {
                Ticket = ticket > 0 ? (ulong)ticket : 0,
                Ok = ok != 0,
                ClosedVolume = closedVolume,
                ClosePrice = closePrice,
            };
            if (ok == 0)
            {
                r.Error = new CmdError
                {
                    Code = errorCode,
                    PlatformCode = platformCode,
                    Message = Wstr.FromUtf16(message),
                    Retryable = retryable != 0,
                };
            }
            return WithShared(-1, s => s.WithBuilder(b =>
            {
                b.CloseResults.Add(r);
                return b.CloseResults.Count;
            }));
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDeal", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDeal(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs)
    {
        return AddDealCore(ticket, orderTicket, positionTicket, symbol,
            orderType, volume, price, profit, swap, commission, comment,
            magic, timeMs, 0, null);
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDealV2", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDealV2(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs, int entry)
    {
        return AddDealCore(ticket, orderTicket, positionTicket, symbol,
            orderType, volume, price, profit, swap, commission, comment,
            magic, timeMs, entry, null);
    }

    [UnmanagedCallersOnly(EntryPoint = "Fleet_AddDealV3", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int AddDealV3(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs, int entry, double fee)
    {
        return AddDealCore(ticket, orderTicket, positionTicket, symbol,
            orderType, volume, price, profit, swap, commission, comment,
            magic, timeMs, entry, fee);
    }

    // ---- shared conversion helpers -------------------------------------------

    private static Position MakePosition(
        long ticket, ushort* symbol, int orderType, double volume,
        double priceOpen, double priceCurrent, double stopLoss,
        double takeProfit, double profit, double swap, double commission,
        ushort* comment, long magic, long openTimeMs) => new()
    {
        Ticket = ticket > 0 ? (ulong)ticket : 0,
        Symbol = Wstr.FromUtf16(symbol),
        Type = orderType,
        Volume = volume,
        PriceOpen = priceOpen,
        PriceCurrent = priceCurrent,
        StopLoss = stopLoss,
        TakeProfit = takeProfit,
        Profit = profit,
        Swap = swap,
        Commission = commission,
        Comment = Wstr.FromUtf16(comment),
        MagicNumber = magic,
        OpenTimeUnixMs = openTimeMs,
    };

    private static PendingOrder MakeOrder(
        long ticket, ushort* symbol, int orderType, double volumeInitial,
        double volumeCurrent, double price, double priceCurrent,
        double stopLimitPrice, double stopLoss, double takeProfit,
        ushort* comment, long magic, long setupTimeMs, long expirationMs) => new()
    {
        Ticket = ticket > 0 ? (ulong)ticket : 0,
        Symbol = Wstr.FromUtf16(symbol),
        Type = orderType,
        VolumeInitial = volumeInitial,
        VolumeCurrent = volumeCurrent,
        Price = price,
        PriceCurrent = priceCurrent,
        StopLimitPrice = stopLimitPrice,
        StopLoss = stopLoss,
        TakeProfit = takeProfit,
        Comment = Wstr.FromUtf16(comment),
        MagicNumber = magic,
        SetupTimeUnixMs = setupTimeMs,
        ExpirationUnixMs = expirationMs,
    };

    private static int AddDealCore(
        long ticket, long orderTicket, long positionTicket, ushort* symbol,
        int orderType, double volume, double price, double profit,
        double swap, double commission, ushort* comment, long magic,
        long timeMs, int entry, double? fee)
    {
        try
        {
            var d = new Deal
            {
                Ticket = ticket > 0 ? (ulong)ticket : 0,
                OrderTicket = orderTicket > 0 ? (ulong)orderTicket : 0,
                PositionTicket = positionTicket > 0 ? (ulong)positionTicket : 0,
                Symbol = Wstr.FromUtf16(symbol),
                Type = orderType,
                Volume = volume,
                Price = price,
                Profit = profit,
                Swap = swap,
                Commission = commission,
                Comment = Wstr.FromUtf16(comment),
                MagicNumber = magic,
                TimeUnixMs = timeMs,
                Entry = entry,
                Fee = fee,
            };
            return WithShared(-1, s => s.WithBuilder(b =>
            {
                b.Deals.Add(d);
                return b.Deals.Count;
            }));
        }
        catch
        {
            return -1;
        }
    }
}

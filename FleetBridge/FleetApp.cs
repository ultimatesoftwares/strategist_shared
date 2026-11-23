using System.Globalization;
using QuickFix;
using QuickFix.Fields;

namespace FleetBridge;

/// <summary>
/// QuickFIX initiator application: terminal-plane session lifecycle (Hello
/// in the Logon, Welcome in the Logon ack) and engine->EA command
/// flattening. Port of bridge/src/fix_app.cpp.
/// </summary>
internal sealed class FleetApp : IApplication
{
    /// <summary>Reported in the Hello (tag 20012); bump on connector releases.</summary>
    public const string ConnectorVersion = "0.2.0";

    private readonly BridgeConfig _cfg;
    private readonly SharedState _shared;

    public FleetApp(BridgeConfig cfg, SharedState shared)
    {
        _cfg = cfg;
        _shared = shared;
    }

    private static string GetS(FieldMap m, int tag)
        => m.IsSetField(tag) ? m.GetString(tag) : "";

    private static long GetI(FieldMap m, int tag)
        => m.IsSetField(tag) &&
           long.TryParse(m.GetString(tag), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
            ? v : 0;

    private static double GetF(FieldMap m, int tag)
        => m.IsSetField(tag) &&
           double.TryParse(m.GetString(tag), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : 0.0;

    /// <summary>
    /// Joins a one-field-per-entry repeating group ("," separated), the
    /// shape the EA splits back apart (same contract as the old proto
    /// `symbols` list).
    /// </summary>
    private static string JoinGroup(Message msg, int noTag, int entryTag)
    {
        int n = msg.GroupCount(noTag);
        if (n == 0)
        {
            return "";
        }
        var parts = new List<string>(n);
        for (int i = 1; i <= n; i++)
        {
            parts.Add(GetS(msg.GetGroup(i, noTag), entryTag));
        }
        return string.Join(',', parts);
    }

    public void OnCreate(SessionID sessionId)
    {
        _shared.SetState(Consts.StateConnecting);
        _shared.LogLine($"connecting to engine {_cfg.Host}:{_cfg.Port}");
    }

    public void OnLogon(SessionID sessionId)
    {
        SessionID sid = sessionId;
        _shared.SetSender(m =>
        {
            try
            {
                return Session.SendToTarget(m, sid);
            }
            catch
            {
                return false;
            }
        });
        _shared.SetState(Consts.StateUp);
        _shared.RecordError("");
        _shared.LogLine("engine session is UP");
    }

    public void OnLogout(SessionID sessionId)
    {
        // Session contract: a reconnect is a brand-new session and the
        // engine re-issues work, so all per-session state is discarded here.
        _shared.ClearSession();
        if (_shared.Shutdown)
        {
            _shared.SetState(Consts.StateDown);
        }
        else
        {
            _shared.SetState(Consts.StateConnecting);
            _shared.RecordError("engine session: logged out");
            _shared.LogLine("engine link down; reconnecting");
        }
    }

    public void ToAdmin(Message message, SessionID sessionId)
    {
        if (message.Header.GetString(Tags.MsgType) != MsgType.LOGON)
        {
            return;
        }
        // Hello rides on the initiator's Logon as custom tags.
        message.SetField(new StringField(Tag.FltPlatform, Numfmt.I64(_cfg.Platform)));
        message.SetField(new StringField(Tag.FltTerminalBuild, _cfg.TerminalBuild));
        message.SetField(new StringField(Tag.FltConnectorVersion, ConnectorVersion));
        message.SetField(new StringField(Tag.FltAccountLogin, Numfmt.I64(_cfg.AccountLogin)));
        message.SetField(new StringField(Tag.FltBrokerServer, _cfg.BrokerServer));
        message.SetField(new StringField(Tag.FltServerTzOffsetMin, Numfmt.I64(_cfg.ServerTzOffsetMinutes)));
        message.SetField(new StringField(Tag.FltMarginMode, Numfmt.I64(_cfg.MarginMode)));
        message.SetField(new StringField(Tag.FltBrokerName, _cfg.BrokerName));
    }

    public void ToApp(Message message, SessionID sessionId)
    {
    }

    public void FromAdmin(Message message, SessionID sessionId)
    {
        if (message.Header.GetString(Tags.MsgType) != MsgType.LOGON)
        {
            return;
        }
        // Welcome rides on the engine's Logon ack. Max tick batch size is
        // ignored: this bridge forwards every tick immediately (batch of one).
        string engineSessionId = GetS(message, Tag.FltSessionId);
        long heartbeatMs = GetI(message, Tag.FltHeartbeatIntervalMs);
        _shared.ApplyWelcome(heartbeatMs);
        _shared.LogLine($"welcome received: session_id={engineSessionId} heartbeat={heartbeatMs}ms");
    }

    public void FromApp(Message message, SessionID sessionId)
    {
        string mt = message.Header.GetString(Tags.MsgType);
        if (mt == Msg.FleetPing)
        {
            // Ping requests an immediate TerminalStatus; no CommandResult.
            _shared.LogLine("ping received; answering with terminal status");
            _shared.Send(Outbound.TerminalStatus(_shared.PingHeartbeat()));
            return;
        }
        HandleCommand(message, mt);
    }

    private void HandleCommand(Message msg, string msgType)
    {
        var cmd = new IncomingCmd
        {
            Id = GetS(msg, Tag.FltCommandId), // empty = fire-and-forget
            DeadlineMs = GetI(msg, Tag.FltDeadlineMs),
        };
        Dictionary<string, Val> f = cmd.Fields;

        // Field names must match the old proto field names exactly: the EA
        // polls them back by name through Fleet_CmdInt/CmdDouble/CmdString.
        switch (msgType)
        {
            case Msg.SubscribeSymbols:
                cmd.CmdType = Consts.CmdSubscribe;
                f["symbols"] = Val.Str(JoinGroup(msg, Tag.NoFltSymbolNames, Tag.FltSymbolName));
                break;
            case Msg.UnsubscribeSymbols:
                cmd.CmdType = Consts.CmdUnsubscribe;
                f["symbols"] = Val.Str(JoinGroup(msg, Tag.NoFltSymbolNames, Tag.FltSymbolName));
                break;
            case Msg.SubscribeDepth:
                cmd.CmdType = Consts.CmdSubscribeDepth;
                f["symbols"] = Val.Str(JoinGroup(msg, Tag.NoFltSymbolNames, Tag.FltSymbolName));
                break;
            case Msg.UnsubscribeDepth:
                cmd.CmdType = Consts.CmdUnsubscribeDepth;
                f["symbols"] = Val.Str(JoinGroup(msg, Tag.NoFltSymbolNames, Tag.FltSymbolName));
                break;
            case Msg.PlaceOrder:
                cmd.CmdType = Consts.CmdPlaceOrder;
                cmd.ClientOrderId = GetS(msg, Tag.FltClientOrderId);
                f["symbol"] = Val.Str(GetS(msg, Tag.FltOrderSymbol));
                f["type"] = Val.I64(GetI(msg, Tag.FltOrderType));
                f["volume"] = Val.F64(GetF(msg, Tag.FltOrderVolume));
                f["price"] = Val.F64(GetF(msg, Tag.FltOrderPrice));
                f["stop_limit_price"] = Val.F64(GetF(msg, Tag.FltOrderStopLimitPrice));
                f["stop_loss"] = Val.F64(GetF(msg, Tag.FltOrderStopLoss));
                f["take_profit"] = Val.F64(GetF(msg, Tag.FltOrderTakeProfit));
                f["deviation_points"] = Val.I64(GetI(msg, Tag.FltDeviationPoints));
                f["magic_number"] = Val.I64(GetI(msg, Tag.FltOrderMagic));
                f["comment"] = Val.Str(GetS(msg, Tag.FltOrderComment));
                f["expiration_unix_ms"] = Val.I64(GetI(msg, Tag.FltOrderExpirationMs));
                f["client_order_id"] = Val.Str(cmd.ClientOrderId);
                break;
            case Msg.ClosePosition:
                cmd.CmdType = Consts.CmdClosePosition;
                f["ticket"] = Val.I64(GetI(msg, Tag.FltTicket));
                f["volume"] = Val.F64(GetF(msg, Tag.FltVolume));
                f["deviation_points"] = Val.I64(GetI(msg, Tag.FltDeviationPoints));
                break;
            case Msg.ClosePositionsBatch:
                cmd.CmdType = Consts.CmdClosePositionsBatch;
                f["tickets"] = Val.Str(JoinGroup(msg, Tag.NoFltTickets, Tag.FltTicketEntry));
                f["symbol"] = Val.Str(GetS(msg, Tag.FltFilterSymbol));
                f["magic_number"] = Val.I64(GetI(msg, Tag.FltFilterMagic));
                f["deviation_points"] = Val.I64(GetI(msg, Tag.FltDeviationPoints));
                break;
            case Msg.ModifyPosition:
                cmd.CmdType = Consts.CmdModifyPosition;
                f["ticket"] = Val.I64(GetI(msg, Tag.FltTicket));
                f["stop_loss"] = Val.F64(GetF(msg, Tag.FltStopLoss));
                f["take_profit"] = Val.F64(GetF(msg, Tag.FltTakeProfit));
                break;
            case Msg.ModifyOrder:
                cmd.CmdType = Consts.CmdModifyOrder;
                f["ticket"] = Val.I64(GetI(msg, Tag.FltTicket));
                f["price"] = Val.F64(GetF(msg, Tag.FltPrice));
                f["stop_limit_price"] = Val.F64(GetF(msg, Tag.FltStopLimitPrice));
                f["stop_loss"] = Val.F64(GetF(msg, Tag.FltStopLoss));
                f["take_profit"] = Val.F64(GetF(msg, Tag.FltTakeProfit));
                f["expiration_unix_ms"] = Val.I64(GetI(msg, Tag.FltExpirationMs));
                break;
            case Msg.CancelOrder:
                cmd.CmdType = Consts.CmdCancelOrder;
                f["ticket"] = Val.I64(GetI(msg, Tag.FltTicket));
                break;
            case Msg.GetAccountInfo:
                cmd.CmdType = Consts.CmdGetAccountInfo;
                break;
            case Msg.GetPositions:
                cmd.CmdType = Consts.CmdGetPositions;
                f["symbol"] = Val.Str(GetS(msg, Tag.FltFilterSymbol));
                f["magic_number"] = Val.I64(GetI(msg, Tag.FltFilterMagic));
                break;
            case Msg.GetPendingOrders:
                cmd.CmdType = Consts.CmdGetPendingOrders;
                f["symbol"] = Val.Str(GetS(msg, Tag.FltFilterSymbol));
                f["magic_number"] = Val.I64(GetI(msg, Tag.FltFilterMagic));
                break;
            case Msg.GetSymbolInfo:
                cmd.CmdType = Consts.CmdGetSymbolInfo;
                f["symbols"] = Val.Str(JoinGroup(msg, Tag.NoFltSymbolNames, Tag.FltSymbolName));
                break;
            case Msg.GetHistory:
                cmd.CmdType = Consts.CmdGetHistory;
                f["symbol"] = Val.Str(GetS(msg, Tag.FltHistSymbol));
                f["timeframe"] = Val.I64(GetI(msg, Tag.FltHistTimeframe));
                f["from_unix_ms"] = Val.I64(GetI(msg, Tag.FltHistFromMs));
                f["to_unix_ms"] = Val.I64(GetI(msg, Tag.FltHistToMs));
                f["count"] = Val.I64(GetI(msg, Tag.FltHistCount));
                break;
            case Msg.GetDealsHistory:
                cmd.CmdType = Consts.CmdGetDealsHistory;
                f["from_unix_ms"] = Val.I64(GetI(msg, Tag.FltHistFromMs));
                f["to_unix_ms"] = Val.I64(GetI(msg, Tag.FltHistToMs));
                f["symbol"] = Val.Str(GetS(msg, Tag.FltFilterSymbol));
                f["magic_number"] = Val.I64(GetI(msg, Tag.FltFilterMagic));
                break;
            default:
                _shared.LogLine($"unsupported command {cmd.Id} ({msgType}); answered UNSUPPORTED");
                if (cmd.Id.Length != 0)
                {
                    _shared.Send(Outbound.ErrorResult(
                        cmd.Id, Consts.ErrUnsupported, 0, "unsupported command", false));
                }
                return;
        }

        _shared.LogLine($"command received: id={cmd.Id} type={cmd.CmdType}");
        _shared.PushCommand(cmd);
    }
}

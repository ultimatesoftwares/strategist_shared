using QuickFix;
using QuickFix.Fields;

namespace FleetBridge;

/// <summary>
/// Model -> QuickFix.Message builders for every event the bridge sends up
/// the terminal plane (FIX44-FLEET dialect). Pure functions: no session
/// access, so tests can assert on the produced messages directly.
/// Port of bridge/src/outbound.cpp.
/// </summary>
internal static class Outbound
{
    private static Message Make(string msgType)
    {
        var m = new Message();
        m.Header.SetField(new MsgType(msgType));
        return m;
    }

    private static void SetS(FieldMap m, int tag, string v) => m.SetField(new StringField(tag, v));
    private static void SetI(FieldMap m, int tag, long v) => m.SetField(new StringField(tag, Numfmt.I64(v)));
    private static void SetU(FieldMap m, int tag, ulong v) => m.SetField(new StringField(tag, Numfmt.U64(v)));
    private static void SetD(FieldMap m, int tag, double v) => m.SetField(new StringField(tag, Numfmt.F64(v)));
    private static void SetB(FieldMap m, int tag, bool v) => m.SetField(new StringField(tag, v ? "Y" : "N"));

    private static void FillPosition(FieldMap g, Position p)
    {
        SetU(g, Tag.FltPosTicket, p.Ticket);
        SetS(g, Tag.FltPosSymbol, p.Symbol);
        SetI(g, Tag.FltPosType, p.Type);
        SetD(g, Tag.FltPosVolume, p.Volume);
        SetD(g, Tag.FltPosPriceOpen, p.PriceOpen);
        SetD(g, Tag.FltPosPriceCurrent, p.PriceCurrent);
        SetD(g, Tag.FltPosStopLoss, p.StopLoss);
        SetD(g, Tag.FltPosTakeProfit, p.TakeProfit);
        SetD(g, Tag.FltPosProfit, p.Profit);
        SetD(g, Tag.FltPosSwap, p.Swap);
        SetD(g, Tag.FltPosCommission, p.Commission);
        SetS(g, Tag.FltPosComment, p.Comment);
        SetI(g, Tag.FltPosMagic, p.MagicNumber);
        SetI(g, Tag.FltPosOpenTimeMs, p.OpenTimeUnixMs);
    }

    private static void FillOrder(FieldMap g, PendingOrder o)
    {
        SetU(g, Tag.FltOrdTicket, o.Ticket);
        SetS(g, Tag.FltOrdSymbol, o.Symbol);
        SetI(g, Tag.FltOrdType, o.Type);
        SetD(g, Tag.FltOrdVolumeInitial, o.VolumeInitial);
        SetD(g, Tag.FltOrdVolumeCurrent, o.VolumeCurrent);
        SetD(g, Tag.FltOrdPrice, o.Price);
        SetD(g, Tag.FltOrdPriceCurrent, o.PriceCurrent);
        SetD(g, Tag.FltOrdStopLimitPrice, o.StopLimitPrice);
        SetD(g, Tag.FltOrdStopLoss, o.StopLoss);
        SetD(g, Tag.FltOrdTakeProfit, o.TakeProfit);
        SetS(g, Tag.FltOrdComment, o.Comment);
        SetI(g, Tag.FltOrdMagic, o.MagicNumber);
        SetI(g, Tag.FltOrdSetupTimeMs, o.SetupTimeUnixMs);
        SetI(g, Tag.FltOrdExpirationMs, o.ExpirationUnixMs);
    }

    private static void FillDeal(FieldMap g, Deal d)
    {
        SetU(g, Tag.FltDealTicket, d.Ticket);
        SetU(g, Tag.FltDealOrderTicket, d.OrderTicket);
        SetU(g, Tag.FltDealPositionTicket, d.PositionTicket);
        SetS(g, Tag.FltDealSymbol, d.Symbol);
        SetI(g, Tag.FltDealType, d.Type);
        SetD(g, Tag.FltDealVolume, d.Volume);
        SetD(g, Tag.FltDealPrice, d.Price);
        SetD(g, Tag.FltDealProfit, d.Profit);
        SetD(g, Tag.FltDealSwap, d.Swap);
        SetD(g, Tag.FltDealCommission, d.Commission);
        SetS(g, Tag.FltDealComment, d.Comment);
        SetI(g, Tag.FltDealMagic, d.MagicNumber);
        SetI(g, Tag.FltDealTimeMs, d.TimeUnixMs);
        SetI(g, Tag.FltDealEntry, d.Entry);
        if (d.Fee.HasValue)
        {
            SetD(g, Tag.FltDealFee, d.Fee.Value);
        }
    }

    /// <summary>CommandResult (U6) skeleton: envelope fields shared by every variant.</summary>
    private static Message ResultBase(string commandId, bool ok, int wirePayloadType)
    {
        Message m = Make(Msg.CommandResult);
        SetS(m, Tag.FltCommandId, commandId);
        SetB(m, Tag.FltOk, ok);
        SetI(m, Tag.FltPayloadType, wirePayloadType);
        return m;
    }

    public static Message TickBatch(Tick tick, AccountSnapshot account)
    {
        Message m = Make(Msg.TickBatch);
        SetD(m, Tag.FltAccBalance, account.Balance);
        SetD(m, Tag.FltAccEquity, account.Equity);
        SetD(m, Tag.FltAccMargin, account.Margin);
        SetD(m, Tag.FltAccMarginFree, account.MarginFree);
        var g = new Group(Tag.NoFltTicks, Tag.FltTickSymbol);
        SetS(g, Tag.FltTickSymbol, tick.Symbol);
        SetD(g, Tag.FltTickBid, tick.Bid);
        SetD(g, Tag.FltTickAsk, tick.Ask);
        SetD(g, Tag.FltTickLast, tick.Last);
        SetU(g, Tag.FltTickVolume, tick.Volume);
        SetI(g, Tag.FltTickTimeMs, tick.TimeUnixMs);
        SetI(g, Tag.FltTickSpreadPoints, tick.SpreadPoints);
        m.AddGroup(g);
        return m;
    }

    public static Message TerminalStatus(Heartbeat hb)
    {
        Message m = Make(Msg.TerminalStatus);
        SetI(m, Tag.FltStatusTimeMs, hb.UnixMs);
        SetB(m, Tag.FltTerminalConnected, hb.TerminalConnected);
        SetD(m, Tag.FltEquity, hb.Equity);
        SetD(m, Tag.FltBalance, hb.Balance);
        SetI(m, Tag.FltPendingOrderCount, hb.PendingOrders);
        foreach (Position p in hb.Positions)
        {
            var g = new Group(Tag.NoFltPositions, Tag.FltPosTicket);
            FillPosition(g, p);
            m.AddGroup(g);
        }
        return m;
    }

    public static Message DepthSnapshot(MarketDepth depth)
    {
        Message m = Make(Msg.MarketDepthSnapshot);
        SetS(m, Tag.FltDepthSymbol, depth.Symbol);
        SetI(m, Tag.FltDepthTimeMs, depth.TimeUnixMs);
        foreach (BookEntry e in depth.Entries)
        {
            var g = new Group(Tag.NoFltBookEntries, Tag.FltBookSide);
            SetI(g, Tag.FltBookSide, e.Side);
            SetD(g, Tag.FltBookPrice, e.Price);
            SetD(g, Tag.FltBookVolume, e.Volume);
            m.AddGroup(g);
        }
        return m;
    }

    public static Message TradeEventPosition(int kind, long timeUnixMs, Position pos)
    {
        Message m = Make(Msg.TradeEvent);
        SetI(m, Tag.FltTradeKind, kind);
        SetI(m, Tag.FltTradeTimeMs, timeUnixMs);
        SetI(m, Tag.FltTradeSubject, 1); // 1 = position (flat FltPos* fields)
        FillPosition(m, pos);
        return m;
    }

    public static Message TradeEventOrder(int kind, long timeUnixMs, PendingOrder ord)
    {
        Message m = Make(Msg.TradeEvent);
        SetI(m, Tag.FltTradeKind, kind);
        SetI(m, Tag.FltTradeTimeMs, timeUnixMs);
        SetI(m, Tag.FltTradeSubject, 2); // 2 = pending order (flat FltOrd*)
        FillOrder(m, ord);
        return m;
    }

    public static Message LogEvent(int level, string message, long timeUnixMs)
    {
        Message m = Make(Msg.LogEvent);
        SetI(m, Tag.FltLogLevel, level);
        SetS(m, Tag.FltLogMessage, message);
        SetI(m, Tag.FltLogTimeMs, timeUnixMs);
        return m;
    }

    // CommandResult (U6) variants. Exactly one per non-empty command id.

    public static Message AckResult(string commandId)
        => ResultBase(commandId, true, Consts.WirePayloadNone);

    public static Message ErrorResult(
        string commandId, int code, int platformCode, string message, bool retryable)
    {
        Message m = ResultBase(commandId, false, Consts.WirePayloadNone);
        SetI(m, Tag.FltErrorCode, code);
        SetI(m, Tag.FltErrorPlatformCode, platformCode);
        SetS(m, Tag.FltErrorMessage, message);
        SetB(m, Tag.FltErrorRetryable, retryable);
        return m;
    }

    public static Message OrderResultMsg(string commandId, OrderResult r)
    {
        Message m = ResultBase(commandId, true, Consts.WirePayloadOrder);
        SetU(m, Tag.FltOrderTicket, r.OrderTicket);
        SetU(m, Tag.FltPositionTicket, r.PositionTicket);
        SetD(m, Tag.FltExecutedPrice, r.ExecutedPrice);
        SetD(m, Tag.FltExecutedVolume, r.ExecutedVolume);
        SetS(m, Tag.FltClientOrderId, r.ClientOrderId);
        return m;
    }

    public static Message AccountInfoResult(string commandId, AccountInfo info)
    {
        Message m = ResultBase(commandId, true, Consts.WirePayloadAccountInfo);
        SetI(m, Tag.FltAccountLogin, info.Login);
        SetS(m, Tag.FltBrokerServer, info.BrokerServer);
        SetS(m, Tag.FltAccBrokerCompany, info.BrokerCompany);
        SetS(m, Tag.FltAccCurrency, info.Currency);
        SetD(m, Tag.FltAccBalance, info.Balance);
        SetD(m, Tag.FltAccEquity, info.Equity);
        SetD(m, Tag.FltAccMargin, info.Margin);
        SetD(m, Tag.FltAccMarginFree, info.MarginFree);
        SetD(m, Tag.FltAccMarginLevel, info.MarginLevel);
        SetI(m, Tag.FltAccLeverage, info.Leverage);
        SetB(m, Tag.FltAccTradeAllowed, info.TradeAllowed);
        SetS(m, Tag.FltAccName, info.AccountName);
        SetS(m, Tag.FltAccEnvironment, info.Environment);
        return m;
    }

    /// <summary>
    /// Builds a multi-row CommandResult from the EA's ResultBuilder.
    /// <paramref name="payloadType"/> is the EA-facing PAYLOAD_* id; returns
    /// null when it is unknown (mirrors Fleet_EndResult's -1).
    /// </summary>
    public static Message? RowsResult(ResultBuilder b, int payloadType)
    {
        switch (payloadType)
        {
            case Consts.PayloadHistory:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadHistory);
                foreach (Bar bar in b.Bars)
                {
                    var g = new Group(Tag.NoFltBars, Tag.FltBarTimeMs);
                    SetI(g, Tag.FltBarTimeMs, bar.TimeUnixMs);
                    SetD(g, Tag.FltBarOpen, bar.Open);
                    SetD(g, Tag.FltBarHigh, bar.High);
                    SetD(g, Tag.FltBarLow, bar.Low);
                    SetD(g, Tag.FltBarClose, bar.Close);
                    SetU(g, Tag.FltBarTickVolume, bar.TickVolume);
                    SetU(g, Tag.FltBarRealVolume, bar.RealVolume);
                    SetI(g, Tag.FltBarSpread, bar.Spread);
                    m.AddGroup(g);
                }
                return m;
            }
            case Consts.PayloadPositions:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadPositions);
                foreach (Position p in b.Positions)
                {
                    var g = new Group(Tag.NoFltPositions, Tag.FltPosTicket);
                    FillPosition(g, p);
                    m.AddGroup(g);
                }
                return m;
            }
            case Consts.PayloadPending:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadPending);
                foreach (PendingOrder o in b.Orders)
                {
                    var g = new Group(Tag.NoFltPendingOrders, Tag.FltOrdTicket);
                    FillOrder(g, o);
                    m.AddGroup(g);
                }
                return m;
            }
            case Consts.PayloadSymbols:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadSymbols);
                foreach (SymbolInfo s in b.Symbols)
                {
                    var g = new Group(Tag.NoFltSymbolInfos, Tag.FltSymName);
                    SetS(g, Tag.FltSymName, s.Symbol);
                    SetS(g, Tag.FltSymDescription, s.Description);
                    SetI(g, Tag.FltSymDigits, s.Digits);
                    SetD(g, Tag.FltSymPoint, s.Point);
                    SetD(g, Tag.FltSymTickSize, s.TickSize);
                    SetD(g, Tag.FltSymTickValue, s.TickValue);
                    SetD(g, Tag.FltSymContractSize, s.ContractSize);
                    SetD(g, Tag.FltSymVolumeMin, s.VolumeMin);
                    SetD(g, Tag.FltSymVolumeMax, s.VolumeMax);
                    SetD(g, Tag.FltSymVolumeStep, s.VolumeStep);
                    SetI(g, Tag.FltSymStopsLevelPoints, s.StopsLevelPoints);
                    SetI(g, Tag.FltSymFreezeLevelPoints, s.FreezeLevelPoints);
                    SetD(g, Tag.FltSymMarginRequired, s.MarginRequired);
                    SetS(g, Tag.FltSymCurrencyBase, s.CurrencyBase);
                    SetS(g, Tag.FltSymCurrencyProfit, s.CurrencyProfit);
                    SetS(g, Tag.FltSymCurrencyMargin, s.CurrencyMargin);
                    SetB(g, Tag.FltSymTradeAllowed, s.TradeAllowed);
                    SetI(g, Tag.FltSymSpreadPoints, s.SpreadPoints);
                    m.AddGroup(g);
                }
                return m;
            }
            case Consts.PayloadDeals:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadDeals);
                foreach (Deal d in b.Deals)
                {
                    var g = new Group(Tag.NoFltDeals, Tag.FltDealTicket);
                    FillDeal(g, d);
                    m.AddGroup(g);
                }
                return m;
            }
            case Consts.PayloadBatchClose:
            {
                Message m = ResultBase(b.Id, true, Consts.WirePayloadBatchClose);
                foreach (CloseResult c in b.CloseResults)
                {
                    var g = new Group(Tag.NoFltCloseResults, Tag.FltCloseTicket);
                    SetU(g, Tag.FltCloseTicket, c.Ticket);
                    SetB(g, Tag.FltCloseOk, c.Ok);
                    SetD(g, Tag.FltCloseVolume, c.ClosedVolume);
                    SetD(g, Tag.FltClosePrice, c.ClosePrice);
                    if (!c.Ok && c.Error is { } e)
                    {
                        SetI(g, Tag.FltCloseErrorCode, e.Code);
                        SetI(g, Tag.FltCloseErrorPlatformCode, e.PlatformCode);
                        SetS(g, Tag.FltCloseErrorMessage, e.Message);
                        SetB(g, Tag.FltCloseErrorRetryable, e.Retryable);
                    }
                    m.AddGroup(g);
                }
                return m;
            }
            default:
                return null;
        }
    }
}

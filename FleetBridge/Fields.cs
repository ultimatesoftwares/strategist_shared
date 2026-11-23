namespace FleetBridge;

/// <summary>
/// FIX44-FLEET dialect constants: custom MsgTypes and tag numbers.
/// Single source of truth is spec/FIX44-FLEET.xml (copied from
/// strategist_bot/shared/Fix); keep this file in lockstep with it.
/// Mirrors the engine-side FleetFixCodec constants.
/// </summary>
internal static class Msg
{
    // terminal plane: events up (bridge -> engine)
    public const string TerminalStatus = "U2";
    public const string TickBatch = "U3";
    public const string MarketDepthSnapshot = "U4";
    public const string TradeEvent = "U5";
    public const string CommandResult = "U6";
    public const string LogEvent = "U24";
    // terminal plane: commands down (engine -> bridge)
    public const string SubscribeSymbols = "U7";
    public const string UnsubscribeSymbols = "U8";
    public const string SubscribeDepth = "U9";
    public const string UnsubscribeDepth = "U10";
    public const string PlaceOrder = "U11";
    public const string ClosePosition = "U12";
    public const string ClosePositionsBatch = "U13";
    public const string ModifyPosition = "U14";
    public const string ModifyOrder = "U15";
    public const string CancelOrder = "U16";
    public const string GetAccountInfo = "U17";
    public const string GetPositions = "U18";
    public const string GetPendingOrders = "U19";
    public const string GetSymbolInfo = "U20";
    public const string GetHistory = "U21";
    public const string GetDealsHistory = "U22";
    public const string FleetPing = "U23";
}

internal static class Tag
{
    // command/result envelope
    public const int FltCommandId = 20001;
    public const int FltDeadlineMs = 20002;
    public const int FltOk = 20050;
    public const int FltErrorCode = 20051;
    public const int FltErrorPlatformCode = 20052;
    public const int FltErrorMessage = 20053;
    public const int FltErrorRetryable = 20054;
    public const int FltPayloadType = 20055;

    // hello / welcome (logon)
    public const int FltPlatform = 20010;
    public const int FltTerminalBuild = 20011;
    public const int FltConnectorVersion = 20012;
    public const int FltAccountLogin = 20013;
    public const int FltBrokerServer = 20014;
    public const int FltServerTzOffsetMin = 20015;
    public const int FltMarginMode = 20016;
    public const int FltBrokerName = 20017;
    public const int FltSessionId = 20020;
    public const int FltMaxTickBatchSize = 20021;
    public const int FltHeartbeatIntervalMs = 20022;

    // terminal status
    public const int FltTerminalConnected = 20030;
    public const int FltEquity = 20031;
    public const int FltBalance = 20032;
    public const int FltPendingOrderCount = 20033;
    public const int FltStatusTimeMs = 20034;

    // positions (group + flat in TradeEvent)
    public const int NoFltPositions = 20100;
    public const int FltPosTicket = 20101;
    public const int FltPosSymbol = 20102;
    public const int FltPosType = 20103;
    public const int FltPosVolume = 20104;
    public const int FltPosPriceOpen = 20105;
    public const int FltPosPriceCurrent = 20106;
    public const int FltPosStopLoss = 20107;
    public const int FltPosTakeProfit = 20108;
    public const int FltPosProfit = 20109;
    public const int FltPosSwap = 20110;
    public const int FltPosCommission = 20111;
    public const int FltPosComment = 20112;
    public const int FltPosMagic = 20113;
    public const int FltPosOpenTimeMs = 20114;

    // ticks
    public const int NoFltTicks = 20200;
    public const int FltTickSymbol = 20201;
    public const int FltTickBid = 20202;
    public const int FltTickAsk = 20203;
    public const int FltTickLast = 20204;
    public const int FltTickVolume = 20205;
    public const int FltTickTimeMs = 20206;
    public const int FltTickSpreadPoints = 20207;
    public const int FltAccBalance = 20210;
    public const int FltAccEquity = 20211;
    public const int FltAccMargin = 20212;
    public const int FltAccMarginFree = 20213;

    // depth
    public const int FltDepthSymbol = 20220;
    public const int FltDepthTimeMs = 20221;
    public const int NoFltBookEntries = 20222;
    public const int FltBookSide = 20223;
    public const int FltBookPrice = 20224;
    public const int FltBookVolume = 20225;

    // trade events
    public const int FltTradeKind = 20240;
    public const int FltTradeTimeMs = 20241;
    public const int FltTradeSubject = 20242;

    // pending orders (group + flat in TradeEvent)
    public const int FltOrdTicket = 20260;
    public const int FltOrdSymbol = 20261;
    public const int FltOrdType = 20262;
    public const int FltOrdVolumeInitial = 20263;
    public const int FltOrdVolumeCurrent = 20264;
    public const int FltOrdPrice = 20265;
    public const int FltOrdPriceCurrent = 20266;
    public const int FltOrdStopLimitPrice = 20267;
    public const int FltOrdStopLoss = 20268;
    public const int FltOrdTakeProfit = 20269;
    public const int FltOrdComment = 20270;
    public const int FltOrdMagic = 20271;
    public const int FltOrdSetupTimeMs = 20272;
    public const int FltOrdExpirationMs = 20273;

    // deals (group + flat in TradeEvent)
    public const int FltDealTicket = 20280;
    public const int FltDealOrderTicket = 20281;
    public const int FltDealPositionTicket = 20282;
    public const int FltDealSymbol = 20283;
    public const int FltDealType = 20284;
    public const int FltDealVolume = 20285;
    public const int FltDealPrice = 20286;
    public const int FltDealProfit = 20287;
    public const int FltDealSwap = 20288;
    public const int FltDealCommission = 20289;
    public const int FltDealComment = 20290;
    public const int FltDealMagic = 20291;
    public const int FltDealTimeMs = 20292;
    public const int FltDealEntry = 20293;
    public const int FltDealFee = 20294;

    // command result payloads
    public const int FltOrderTicket = 20300;
    public const int FltPositionTicket = 20301;
    public const int FltExecutedPrice = 20302;
    public const int FltExecutedVolume = 20303;
    public const int FltAccBrokerCompany = 20312;
    public const int FltAccCurrency = 20313;
    public const int FltAccMarginLevel = 20314;
    public const int FltAccLeverage = 20315;
    public const int FltAccTradeAllowed = 20316;
    public const int FltAccName = 20317;
    public const int FltAccEnvironment = 20318;
    public const int NoFltBars = 20330;
    public const int FltBarTimeMs = 20331;
    public const int FltBarOpen = 20332;
    public const int FltBarHigh = 20333;
    public const int FltBarLow = 20334;
    public const int FltBarClose = 20335;
    public const int FltBarTickVolume = 20336;
    public const int FltBarRealVolume = 20337;
    public const int FltBarSpread = 20338;
    public const int NoFltPendingOrders = 20340;
    public const int NoFltSymbolInfos = 20350;
    public const int FltSymName = 20351;
    public const int FltSymDescription = 20352;
    public const int FltSymDigits = 20353;
    public const int FltSymPoint = 20354;
    public const int FltSymTickSize = 20355;
    public const int FltSymTickValue = 20356;
    public const int FltSymContractSize = 20357;
    public const int FltSymVolumeMin = 20358;
    public const int FltSymVolumeMax = 20359;
    public const int FltSymVolumeStep = 20360;
    public const int FltSymStopsLevelPoints = 20361;
    public const int FltSymFreezeLevelPoints = 20362;
    public const int FltSymMarginRequired = 20363;
    public const int FltSymCurrencyBase = 20364;
    public const int FltSymCurrencyProfit = 20365;
    public const int FltSymCurrencyMargin = 20366;
    public const int FltSymTradeAllowed = 20367;
    public const int FltSymSpreadPoints = 20368;
    public const int NoFltDeals = 20370;
    public const int NoFltCloseResults = 20380;
    public const int FltCloseTicket = 20381;
    public const int FltCloseOk = 20382;
    public const int FltCloseVolume = 20383;
    public const int FltClosePrice = 20384;
    public const int FltCloseErrorCode = 20385;
    public const int FltCloseErrorPlatformCode = 20386;
    public const int FltCloseErrorMessage = 20387;
    public const int FltCloseErrorRetryable = 20388;

    // command payloads
    public const int NoFltSymbolNames = 20400;
    public const int FltSymbolName = 20401;
    public const int FltClientOrderId = 20410;
    public const int FltOrderSymbol = 20411;
    public const int FltOrderType = 20412;
    public const int FltOrderVolume = 20413;
    public const int FltOrderPrice = 20414;
    public const int FltOrderStopLimitPrice = 20415;
    public const int FltOrderStopLoss = 20416;
    public const int FltOrderTakeProfit = 20417;
    public const int FltDeviationPoints = 20418;
    public const int FltOrderMagic = 20419;
    public const int FltOrderComment = 20420;
    public const int FltOrderExpirationMs = 20421;
    public const int FltTicket = 20430;
    public const int FltVolume = 20431;
    public const int NoFltTickets = 20440;
    public const int FltTicketEntry = 20441;
    public const int FltFilterSymbol = 20442;
    public const int FltFilterMagic = 20443;
    public const int FltStopLoss = 20450;
    public const int FltTakeProfit = 20451;
    public const int FltPrice = 20452;
    public const int FltStopLimitPrice = 20453;
    public const int FltExpirationMs = 20454;
    public const int FltHistSymbol = 20460;
    public const int FltHistTimeframe = 20461;
    public const int FltHistFromMs = 20462;
    public const int FltHistToMs = 20463;
    public const int FltHistCount = 20464;
    public const int FltPingTimeMs = 20470;
    public const int FltLogLevel = 20480;
    public const int FltLogMessage = 20481;
    public const int FltLogTimeMs = 20482;
}

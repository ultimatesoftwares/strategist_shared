namespace Trading.Core.Messages;

// Payload contracts published on the FIX feed plane (U31 FeedEvent) by the
// Bot, serialized as the JSON payload of each update. Keep them flat and
// serialization-friendly; consumers in other languages bind to the JSON shape.
//
// Streaming messages (book updates, executions, terminal ticks/depth) carry a
// `Source` envelope field identifying where the data came from, e.g.
// "ctrader:fx-demo", "sim:sim-demo", "mt5:terminal-7".

/// <summary>One L2 book delta from a broker feed (qty 0 removes the level).</summary>
public sealed record BookUpdateMessage(
    string Source,
    string Account,
    string Broker,
    string Symbol,
    string Side,
    double Price,
    ulong Qty,
    ulong TsNanos,
    DateTime TimestampUtc);

/// <summary>Last price and optional quote fields from a market-data-only feed.</summary>
public sealed record MarketPriceMessage(
    string Source, string Account, string Broker, string Symbol,
    double Price, double? Bid, double? Ask, double? Volume, ulong TsNanos, DateTime TimestampUtc);

/// <summary>One execution report (ack/fill/cancel/reject) from a broker.</summary>
public sealed record ExecutionMessage(
    string Source,
    string Account,
    string Broker,
    string Symbol,
    ulong OrderId,
    string Status,
    string Side,
    double FillPrice,
    ulong FillQty,
    ulong LeavesQty,
    DateTime TimestampUtc);

/// <summary>One tick from an MT4/MT5 terminal (fleet protocol TickBatch).</summary>
public sealed record TerminalTickMessage(
    string Source,
    string TerminalId,
    long AccountLogin,
    string Symbol,
    double Bid,
    double Ask,
    double Last,
    ulong Volume,
    long TimeUnixMs,
    DateTime TimestampUtc);

/// <summary>A depth-of-market snapshot from an MT5 terminal.</summary>
public sealed record TerminalDepthMessage(
    string Source,
    string TerminalId,
    string Symbol,
    long TimeUnixMs,
    IReadOnlyList<DepthLevelMessage> Levels,
    DateTime TimestampUtc);

public sealed record DepthLevelMessage(string Side, double Price, double Volume);

/// <summary>Periodic per-account state snapshot.</summary>
public sealed record AccountSnapshotMessage(
    string Account,
    string Broker,
    DateTime TimestampUtc,
    long OrdersPlaced,
    long OrdersCancelled,
    long Fills,
    double RealizedCashFlow,
    IReadOnlyList<SymbolPositionMessage> Positions);

public sealed record SymbolPositionMessage(string Symbol, long PositionLots);

/// <summary>Live non-zero positions of one account, with per-session PnL
/// attribution. EntryPrice/UnrealizedPnl are null when the session's fill
/// ledger does not fully explain the position (e.g. it predates the process
/// or was broker-reconciled); MarkPrice is null when no quote is available.</summary>
public sealed record AccountPositionsMessage(
    string Account,
    DateTime TimestampUtc,
    IReadOnlyList<PositionDetailMessage> Positions);

public sealed record PositionDetailMessage(
    string Symbol,
    long PositionLots,
    double? EntryPrice,
    double? MarkPrice,
    double? UnrealizedPnl);

/// <summary>Live open (working) orders of one account (display-unit prices).</summary>
public sealed record AccountOrdersMessage(
    string Account,
    DateTime TimestampUtc,
    IReadOnlyList<OpenOrderMessage> Orders);

public sealed record OpenOrderMessage(
    ulong OrderId,
    string Symbol,
    string Side,
    double Price,
    ulong Qty,
    ulong FilledQty,
    ulong LeavesQty,
    string Status,
    bool Manual,
    DateTime CreatedUtc);

/// <summary>Top-of-book snapshot for one symbol of one account (display-unit prices).</summary>
public sealed record OrderBookTopMessage(
    string Account,
    string Symbol,
    DateTime TimestampUtc,
    double BidPrice,
    ulong BidQty,
    double AskPrice,
    ulong AskQty);

/// <summary>Aggregate health of the bridge: engine accounts + connected MT terminals.</summary>
public sealed record FleetHealthMessage(
    DateTime TimestampUtc,
    string Status,
    IReadOnlyList<AccountHealthMessage> Accounts,
    IReadOnlyList<TerminalHealthMessage> Terminals);

public sealed record AccountHealthMessage(string Account, string State, string? LastError);

public sealed record TerminalHealthMessage(
    string TerminalId,
    string Platform,
    long AccountLogin,
    bool Connected,
    double Equity,
    double Balance,
    DateTime LastHeartbeatUtc);

namespace Trading.Core;

public enum Side : uint
{
    Buy = 0,
    Sell = 1,
}

public enum OrderStatus : uint
{
    Accepted = 0,
    PartiallyFilled = 1,
    Filled = 2,
    Cancelled = 3,
    Rejected = 4,
}

/// <summary>An L2 book delta. Qty of 0 removes the price level.</summary>
public readonly record struct BookUpdate(
    uint SymbolId, Side Side, long Price, ulong Qty, ulong TsNanos);

/// <summary>A top-of-book quote tick (MT-style bid/ask). Qty of 0 = size unknown.</summary>
public readonly record struct QuoteTick(
    uint SymbolId, long Bid, long Ask, ulong BidQty, ulong AskQty, ulong TsNanos);

/// <summary>
/// A closed OHLC bar. <see cref="TimeUnixMs"/> is the bar's OPEN time in Unix
/// milliseconds (the MetaTrader convention); prices are fixed-point scaled
/// integers like everything else on the engine boundary.
/// </summary>
public readonly record struct Bar(
    uint SymbolId, long TimeUnixMs, long Open, long High, long Low, long Close, ulong Volume);

/// <summary>Execution feedback from a broker, fed back into the engine.</summary>
public readonly record struct ExecutionReport(
    ulong OrderId, uint SymbolId, OrderStatus Status, Side Side,
    long FillPrice, ulong FillQty, ulong LeavesQty, ulong TsNanos,
    ulong BrokerOrderTicket = 0, ulong BrokerPositionTicket = 0,
    string? BrokerOrderId = null, bool IsPositionClose = false);

/// <summary>A command emitted by the engine for the broker to execute.</summary>
public abstract record OrderCommand(ulong OrderId, uint SymbolId)
{
    public sealed record Place(ulong OrderId, uint SymbolId, Side Side, long Price, ulong Qty)
        : OrderCommand(OrderId, SymbolId);

    public sealed record Cancel(ulong OrderId, uint SymbolId)
        : OrderCommand(OrderId, SymbolId);
}

public readonly record struct RiskLimits(long MaxPosition, ulong MaxOrderQty, ulong MaxOpenOrders)
{
    public static RiskLimits Default => new(MaxPosition: 100, MaxOrderQty: 10, MaxOpenOrders: 4);
}

public enum StrategyKind : uint
{
    /// <summary>RETIRED: the built-in demo market maker. Kept for wire/journal
    /// compatibility only — <see cref="Engine.StrategyFactory"/> rejects it;
    /// every trading strategy must be ML-driven.</summary>
    ImbalanceQuoter = 0,
    /// <summary>Never trades; the engine only maintains book/position state.</summary>
    Null = 1,
    /// <summary>Model-driven bar strategy over an ML.NET model.zip (see Engine/MlStrategy.cs for the model contract).</summary>
    Ml = 2,
    /// <summary>Experimental tick-driven ML.NET classifier; intended for observe-only evaluation.</summary>
    TickMl = 3,
    /// <summary>High-frequency edge strategy: quote ticks are aggregated into fixed
    /// micro-bars driving an edge-model decision per closed micro-bar, with a
    /// tick-level stop loss (see Engine/TickEdgeStrategy.cs).</summary>
    TickEdge = 4,
}

/// <summary>Strategy selection + parameters, passed to the engine at creation. 0 = strategy default.</summary>
/// <remarks>
/// <see cref="EntryThresholdBps"/> is, for <see cref="StrategyKind.Ml"/>,
/// the P(up) entry threshold in bps of probability (6000 = 0.60; default).
/// <see cref="ExitThresholdBps"/> and <see cref="MaxHoldBars"/> apply to Ml
/// only: the P(up) exit threshold (default 5000 = 0.50) and the maximum
/// holding time in closed bars (default 5). For <see cref="StrategyKind.TickEdge"/>
/// the thresholds are the edge-mode parameters instead: the minimum predicted
/// edge in bps (entry), the stop loss in bps (exit), the maximum hold in closed
/// micro-bars, and <see cref="TickBarMs"/> as the micro-bar length in
/// milliseconds (0 = 1000). <see cref="ModelPath"/> is required for Ml, TickMl
/// and TickEdge — the only kinds that trade.
/// </remarks>
public readonly record struct StrategyConfig(
    StrategyKind Kind, long EntryThresholdBps = 0, ulong OrderQty = 0, string? ModelPath = null,
    long ExitThresholdBps = 0, long MaxHoldBars = 0, long TickBarMs = 0)
{
    /// <summary>No model, no trading: the default is the non-trading Null
    /// strategy. Trading requires an explicit ML config (Ml/TickMl/TickEdge + model).</summary>
    public static StrategyConfig Default => new(StrategyKind.Null);

    /// <summary>Stable journal descriptor, e.g. "imbalance_quoter" or "ml:models/x.zip".</summary>
    public string Describe() => Kind switch
    {
        StrategyKind.Ml => $"ml:{ModelPath}",
        StrategyKind.TickMl => $"tick_ml:{ModelPath}",
        StrategyKind.TickEdge => $"tick_edge:{ModelPath}",
        StrategyKind.Null => "null",
        _ => "imbalance_quoter",
    };
}

public readonly record struct TopOfBook(long BidPrice, ulong BidQty, long AskPrice, ulong AskQty);

/// <summary>Lifecycle state of a strategy's signal (mirrors engine-core's DecisionStatus).</summary>
public enum DecisionStatus : uint
{
    /// <summary>Still accumulating history; no trading signal yet.</summary>
    Warmup = 0,
    /// <summary>Signal computed normally.</summary>
    Active = 1,
    /// <summary>Strategy is impaired (e.g. model inference failed) and quoting defensively.</summary>
    Degraded = 2,
}

/// <summary>
/// The strategy's signal snapshot behind its most recent book evaluation.
/// Forecast is a dimensionless signal (NaN when none was computed, e.g.
/// during warm-up or degradation).
/// </summary>
public readonly record struct StrategyDecision(
    double Forecast, DecisionStatus Status, string? Reason = null,
    string? SignalKind = null, double? Z60 = null, double? EntryThreshold = null,
    double? ZThreshold = null)
{
    /// <summary>Journal string for <see cref="Status"/>: "warmup" | "active" | "degraded".</summary>
    public string StatusString => Status switch
    {
        DecisionStatus.Active => "active",
        DecisionStatus.Degraded => "degraded",
        _ => "warmup",
    };
}

/// <summary>One aggregated L2 price level (price in the adapter's fixed-point scale).</summary>
public readonly record struct BookLevel(long Price, ulong Qty);

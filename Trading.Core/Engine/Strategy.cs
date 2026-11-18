namespace Trading.Core.Engine;

/// <summary>
/// What a strategy wants to do after seeing new state. The engine assigns
/// order ids and applies risk checks before anything reaches the broker.
/// </summary>
public abstract record Intent
{
    public sealed record Place(uint SymbolId, Side Side, long Price, ulong Qty) : Intent;

    public sealed record Cancel(ulong OrderId, uint SymbolId) : Intent;
}

/// <summary>An order the engine believes is live at the broker.</summary>
public readonly record struct OpenOrder(
    ulong OrderId, uint SymbolId, Side Side, long Price, ulong LeavesQty);

/// <summary>Context handed to the strategy on each event.</summary>
public readonly struct StrategyContext(
    uint symbolId, OrderBook book, long position, IReadOnlyList<OpenOrder> openOrders)
{
    public uint SymbolId { get; } = symbolId;
    public OrderBook Book { get; } = book;
    public long Position { get; } = position;
    public IReadOnlyList<OpenOrder> OpenOrders { get; } = openOrders;
}

/// <summary>
/// Pure decision logic over market state and position. Implementations are
/// single-threaded (the engine enforces its own threading contract).
/// </summary>
public interface IStrategy
{
    /// <summary>Called after every book update for the affected symbol.</summary>
    void OnBook(in StrategyContext ctx, List<Intent> output);

    /// <summary>
    /// Called after every top-of-book quote tick. The engine has already
    /// folded the tick into the book, so the default delegates to
    /// <see cref="OnBook"/> — book strategies keep working when fed ticks.
    /// </summary>
    void OnTick(in StrategyContext ctx, in QuoteTick tick, List<Intent> output) =>
        OnBook(ctx, output);

    /// <summary>
    /// Called once per closed bar. Default: no-op (book/tick strategies
    /// don't care). Bar strategies keep their own history.
    /// </summary>
    void OnBar(in StrategyContext ctx, in Bar bar, List<Intent> output)
    {
    }

    /// <summary>Called after every execution report.</summary>
    void OnExecution(in ExecutionReport report, List<Intent> output)
    {
    }

    /// <summary>
    /// The signal snapshot behind the most recent evaluation (book, tick or
    /// bar), or null when the strategy has no meaningful signal
    /// (<see cref="NullStrategy"/>, or no evaluation has happened yet).
    /// Queried by the host after an event emits intents, to journal why the
    /// orders were placed.
    /// </summary>
    StrategyDecision? LastDecision => null;
}

/// <summary>
/// A strategy that never trades. Useful for accounts that should only
/// maintain book/position state (e.g. market-data collection).
/// </summary>
public sealed class NullStrategy : IStrategy
{
    public void OnBook(in StrategyContext ctx, List<Intent> output)
    {
    }
}

/// <summary>
/// A deliberately simple example strategy: joins the passive side when
/// top-of-book imbalance is strongly in its favor, and flattens by quoting
/// out when holding inventory.
///
/// This is a placeholder for real alpha — the plumbing around it (book,
/// risk, broker routing) is the production-shaped part.
/// </summary>
public sealed class ImbalanceQuoter : IStrategy
{
    /// <summary>Default imbalance entry threshold in bps of top-of-book size.</summary>
    public const long DefaultEntryThresholdBps = 3_000;

    /// <summary>Imbalance threshold in bps of top-of-book size (e.g. 3000 = 30%).</summary>
    public long EntryThresholdBps { get; init; } = DefaultEntryThresholdBps;

    /// <summary>Order size in lots.</summary>
    public ulong OrderQty { get; init; } = 1;

    private StrategyDecision? _lastDecision;

    public StrategyDecision? LastDecision => _lastDecision;

    public void OnBook(in StrategyContext ctx, List<Intent> output)
    {
        if (!ctx.Book.TryGetBestBid(out var bestBid, out _)
            || !ctx.Book.TryGetBestAsk(out var bestAsk, out _)
            || ctx.Book.ImbalanceBps() is not { } imbalance)
        {
            return;
        }
        _lastDecision = new StrategyDecision(imbalance / 10_000.0, DecisionStatus.Active);

        // Hold at most one working order per symbol in this toy strategy.
        if (ctx.OpenOrders.Count > 0)
        {
            // Cancel stale orders that are no longer at the touch.
            foreach (var order in ctx.OpenOrders)
            {
                var touch = order.Side == Side.Buy ? bestBid : bestAsk;
                if (order.Price != touch)
                    output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
            }
            return;
        }

        if (ctx.Position > 0)
        {
            // Long: offer out at the ask.
            output.Add(new Intent.Place(
                ctx.SymbolId, Side.Sell, bestAsk, Math.Min((ulong)ctx.Position, OrderQty)));
        }
        else if (ctx.Position < 0)
        {
            // Short: bid to cover.
            output.Add(new Intent.Place(
                ctx.SymbolId, Side.Buy, bestBid, Math.Min((ulong)(-ctx.Position), OrderQty)));
        }
        else if (imbalance >= EntryThresholdBps)
        {
            // Heavy bid pressure: join the bid.
            output.Add(new Intent.Place(ctx.SymbolId, Side.Buy, bestBid, OrderQty));
        }
        else if (imbalance <= -EntryThresholdBps)
        {
            // Heavy ask pressure: join the offer.
            output.Add(new Intent.Place(ctx.SymbolId, Side.Sell, bestAsk, OrderQty));
        }
    }
}

/// <summary>Builds a strategy from its <see cref="StrategyConfig"/>. A parameter of 0 means "use the default".</summary>
public static class StrategyFactory
{
    /// <summary>
    /// Create the configured strategy. Throws
    /// <see cref="InvalidOperationException"/> for unknown kinds, missing
    /// model paths, out-of-range thresholds, or model-load failures.
    /// </summary>
    public static IStrategy Create(StrategyConfig config) => config.Kind switch
    {
        StrategyKind.ImbalanceQuoter => new ImbalanceQuoter
        {
            EntryThresholdBps = config.EntryThresholdBps > 0
                ? config.EntryThresholdBps
                : ImbalanceQuoter.DefaultEntryThresholdBps,
            OrderQty = config.OrderQty > 0 ? config.OrderQty : 1,
        },
        StrategyKind.Onnx => string.IsNullOrEmpty(config.ModelPath)
            ? throw new InvalidOperationException("onnx strategy requires a model path")
            : OnnxStrategy.FromFile(
                config.ModelPath, config.OrderQty, config.EntryThresholdBps,
                config.ExitThresholdBps, config.MaxHoldBars),
        StrategyKind.Null => new NullStrategy(),
        _ => throw new InvalidOperationException($"unknown strategy kind {(uint)config.Kind}"),
    };
}

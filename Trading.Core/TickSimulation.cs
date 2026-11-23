using Trading.Core.Engine;

namespace Trading.Core;

/// <summary>Configuration for a deterministic, quote-driven IOC fill approximation.</summary>
public sealed record TickSimulationOptions(
    ulong OrderDelayNanos, long SlippageUnits, ulong MaxQuoteGapNanos = 1_000_000_000,
    bool AssumeUnlimitedLiquidity = false);

/// <summary>Simulation output; pending orders at the end have not been settled.</summary>
public sealed record TickSimulationResult(
    IReadOnlyList<TickReplayCommand> Commands,
    IReadOnlyList<ExecutionReport> Executions,
    int PendingOrders);

/// <summary>
/// Single-account replay with delayed, all-or-none IOC fills. This does not model
/// broker latency distributions, queue position, commissions, partial fills or live routing.
/// Without an explicit unlimited-liquidity assumption, a fill requires a reported
/// top-of-book size sufficient for the entire order.
/// </summary>
public static class TickSimulation
{
    private sealed record Pending(OrderCommand.Place Order, ulong SubmittedAt);

    public static TickSimulationResult Run(
        TradingEngine engine, IEnumerable<TickReplayEvent> events, TickSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);
        if (!engine.RequiresAtomicQuotes || engine.OpenOrderCount != 0)
            throw new ArgumentException("Simulation requires a fresh atomic-quote engine.", nameof(engine));
        if (options.SlippageUnits < 0 || options.MaxQuoteGapNanos == 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        var commands = new List<TickReplayCommand>();
        var executions = new List<ExecutionReport>();
        var queued = new Queue<OrderCommand>();
        var pending = new Dictionary<ulong, Pending>();
        var lastQuotes = new Dictionary<uint, ulong>();
        var eventIndex = 0;
        var time = 0UL;
        var first = true;
        void Capture(OrderCommand command)
        {
            commands.Add(new TickReplayCommand(eventIndex, time, command));
            queued.Enqueue(command);
        }
        void Publish(ExecutionReport report)
        {
            executions.Add(report);
            engine.PublishExecution(report);
        }
        void Drain()
        {
            while (queued.TryDequeue(out var command))
            {
                if (command is OrderCommand.Place place)
                    pending.Add(place.OrderId, new Pending(place, time));
                else if (command is OrderCommand.Cancel cancel && pending.Remove(cancel.OrderId, out var item))
                    Publish(new ExecutionReport(cancel.OrderId, cancel.SymbolId, OrderStatus.Cancelled,
                        item.Order.Side, 0, 0, 0, time));
            }
        }

        engine.OrderCommandReceived += Capture;
        try
        {
            foreach (var item in events)
            {
                if (!first && item.TimeNanos < time)
                    throw new InvalidDataException($"Simulation event {eventIndex} is out of order.");
                first = false;
                time = item.TimeNanos;
                switch (item)
                {
                    case TickReplayEvent.Gap gap:
                        if (engine.SymbolName(gap.SymbolId) is null)
                            throw new InvalidDataException($"Simulation event {eventIndex} references an unknown symbol.");
                        lastQuotes.Remove(gap.SymbolId);
                        engine.PublishQuoteGap(gap.SymbolId);
                        Drain();
                        break;
                    case TickReplayEvent.Quote quote:
                        var tick = quote.Tick;
                        if (engine.SymbolName(tick.SymbolId) is null)
                            throw new InvalidDataException($"Simulation event {eventIndex} references an unknown symbol.");
                        if (lastQuotes.TryGetValue(tick.SymbolId, out var previous)
                            && time - previous > options.MaxQuoteGapNanos)
                        {
                            engine.PublishQuoteGap(tick.SymbolId);
                            Drain();
                        }
                        lastQuotes[tick.SymbolId] = time;
                        var due = pending.Values.Where(p => p.Order.SymbolId == tick.SymbolId
                            && time - p.SubmittedAt >= options.OrderDelayNanos).ToArray();
                        var bidRemaining = tick.BidQty;
                        var askRemaining = tick.AskQty;
                        foreach (var order in due)
                        {
                            if (!pending.Remove(order.Order.OrderId))
                                continue;
                            var price = order.Order.Side == Side.Buy
                                ? (Int128)tick.Ask + options.SlippageUnits
                                : (Int128)tick.Bid - options.SlippageUnits;
                            var fills = tick.Bid > 0 && tick.Ask > tick.Bid && price > 0 && price <= long.MaxValue
                                && (options.AssumeUnlimitedLiquidity
                                    || (order.Order.Side == Side.Buy ? askRemaining : bidRemaining) >= order.Order.Qty)
                                && (order.Order.Side == Side.Buy ? price <= order.Order.Price : price >= order.Order.Price);
                            if (fills && !options.AssumeUnlimitedLiquidity)
                            {
                                if (order.Order.Side == Side.Buy)
                                    askRemaining -= order.Order.Qty;
                                else
                                    bidRemaining -= order.Order.Qty;
                            }
                            Publish(new ExecutionReport(order.Order.OrderId, tick.SymbolId,
                                fills ? OrderStatus.Filled : OrderStatus.Rejected, order.Order.Side,
                                fills ? (long)price : 0, fills ? order.Order.Qty : 0, 0, time));
                            Drain();
                        }
                        engine.PublishTick(tick);
                        Drain();
                        break;
                    default:
                        throw new InvalidDataException($"Simulation event {eventIndex} must be a quote or gap.");
                }
                eventIndex++;
            }
        }
        finally
        {
            engine.OrderCommandReceived -= Capture;
        }
        return new TickSimulationResult(commands, executions, pending.Count);
    }
}

using Trading.Core.Engine;

namespace Trading.Core;

/// <summary>One input to a deterministic, single-account engine replay.</summary>
public abstract record TickReplayEvent(ulong TimeNanos)
{
    /// <summary>A complete bid/ask snapshot. TimeNanos is the recorded local arrival time.</summary>
    public sealed record Quote(ulong TimeNanos, QuoteTick Tick) : TickReplayEvent(TimeNanos);

    /// <summary>A recorded execution report; no fills are invented by the replay.</summary>
    public sealed record Execution(ulong TimeNanos, ExecutionReport Report) : TickReplayEvent(TimeNanos);

    /// <summary>An explicitly observed interruption or dropped quote.</summary>
    public sealed record Gap(ulong TimeNanos, uint SymbolId) : TickReplayEvent(TimeNanos);
}

/// <summary>A command emitted by the engine while handling an input event.</summary>
public sealed record TickReplayCommand(int EventIndex, ulong TimeNanos, OrderCommand Command);

/// <summary>
/// Replays supplied atomic quotes and execution reports without wall-clock timing,
/// a broker, or synthetic fills. Use a fresh engine with registered symbol IDs.
/// The caller owns the engine and must supply execution reports corresponding to
/// emitted order IDs, in the order they were observed.
/// </summary>
public static class TickEventReplay
{
    public static IReadOnlyList<TickReplayCommand> Run(
        TradingEngine engine, IEnumerable<TickReplayEvent> events, ulong maxQuoteGapNanos = 1_000_000_000)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(events);
        if (!engine.RequiresAtomicQuotes)
            throw new ArgumentException("Replay requires an atomic-quote strategy.", nameof(engine));
        if (engine.OpenOrderCount != 0)
            throw new ArgumentException("Replay requires an engine with no open orders.", nameof(engine));
        if (maxQuoteGapNanos == 0)
            throw new ArgumentOutOfRangeException(nameof(maxQuoteGapNanos));

        var commands = new List<TickReplayCommand>();
        var lastQuotes = new Dictionary<uint, ulong>();
        var outstanding = new Dictionary<ulong, (OrderCommand.Place Order, ulong Remaining)>();
        var eventIndex = 0;
        var time = 0UL;
        var first = true;
        void Capture(OrderCommand command)
        {
            if (command is OrderCommand.Place place)
                outstanding.Add(command.OrderId, (place, place.Qty));
            commands.Add(new TickReplayCommand(eventIndex, time, command));
        }
        engine.OrderCommandReceived += Capture;
        try
        {
            foreach (var item in events)
            {
                if (!first && item.TimeNanos < time)
                    throw new InvalidDataException($"Replay event {eventIndex} is out of order.");
                first = false;
                time = item.TimeNanos;
                switch (item)
                {
                    case TickReplayEvent.Quote quote:
                        if (engine.SymbolName(quote.Tick.SymbolId) is null)
                            throw new InvalidDataException($"Replay event {eventIndex} references an unknown symbol.");
                        if (lastQuotes.TryGetValue(quote.Tick.SymbolId, out var previous)
                            && time - previous > maxQuoteGapNanos)
                            engine.PublishQuoteGap(quote.Tick.SymbolId);
                        lastQuotes[quote.Tick.SymbolId] = time;
                        engine.PublishTick(quote.Tick);
                        break;
                    case TickReplayEvent.Gap gap:
                        if (engine.SymbolName(gap.SymbolId) is null)
                            throw new InvalidDataException($"Replay event {eventIndex} references an unknown symbol.");
                        lastQuotes.Remove(gap.SymbolId);
                        engine.PublishQuoteGap(gap.SymbolId);
                        break;
                    case TickReplayEvent.Execution execution:
                        if (!outstanding.TryGetValue(execution.Report.OrderId, out var tracked))
                            throw new InvalidDataException($"Replay event {eventIndex} references an unknown order.");
                        var report = execution.Report;
                        if (!Enum.IsDefined(report.Status)
                            || report.SymbolId != tracked.Order.SymbolId || report.Side != tracked.Order.Side
                            || report.FillQty > tracked.Remaining
                            || (report.Status is OrderStatus.Filled or OrderStatus.PartiallyFilled
                                ? report.FillQty == 0 || report.FillPrice <= 0
                                  || report.LeavesQty != tracked.Remaining - report.FillQty
                                  || (report.Status == OrderStatus.Filled) != (report.LeavesQty == 0)
                                : report.FillQty != 0
                                  || (report.Status == OrderStatus.Accepted && report.LeavesQty != tracked.Remaining)))
                            throw new InvalidDataException($"Replay event {eventIndex} has an inconsistent execution report.");
                        engine.PublishExecution(execution.Report);
                        if (execution.Report.Status is OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Cancelled)
                            outstanding.Remove(execution.Report.OrderId);
                        else if (execution.Report.Status == OrderStatus.PartiallyFilled)
                            outstanding[execution.Report.OrderId] = (tracked.Order, report.LeavesQty);
                        break;
                    default:
                        throw new InvalidDataException($"Replay event {eventIndex} has an unknown type.");
                }
                eventIndex++;
            }
        }
        finally
        {
            engine.OrderCommandReceived -= Capture;
        }
        return commands;
    }
}

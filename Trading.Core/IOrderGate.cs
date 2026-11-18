namespace Trading.Core;

/// <summary>Verdict of an <see cref="IOrderGate"/> for one outbound order.</summary>
public readonly record struct OrderGateDecision(bool Allowed, string? Reason)
{
    public static OrderGateDecision Allow => new(true, null);
    public static OrderGateDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// A pre-trade check applied by <see cref="EngineHost"/> to every order the
/// strategy wants to place, before it reaches the broker. Rejected orders are
/// fed back into the engine as <see cref="OrderStatus.Rejected"/> so its
/// bookkeeping stays consistent. Cancels are never gated (reducing exposure
/// must always be possible).
///
/// Called on the engine thread only.
/// </summary>
public interface IOrderGate
{
    OrderGateDecision Evaluate(in OrderCommand.Place order);
}

/// <summary>Chains gates; the first denial wins.</summary>
public sealed class CompositeOrderGate : IOrderGate
{
    private readonly IOrderGate[] _gates;

    public CompositeOrderGate(params IOrderGate[] gates) => _gates = gates;

    public OrderGateDecision Evaluate(in OrderCommand.Place order)
    {
        foreach (var gate in _gates)
        {
            var decision = gate.Evaluate(order);
            if (!decision.Allowed)
                return decision;
        }
        return OrderGateDecision.Allow;
    }
}

/// <summary>
/// Denies strategy placements while the symbol's market is closed, based on
/// an <see cref="IMarketSessionSource"/> (cached broker schedules/clocks).
/// Unknown session state (no source, or the source has no schedule for the
/// symbol) allows the order - gating must never be stricter than the broker.
/// </summary>
public sealed class MarketSessionGate : IOrderGate
{
    private readonly Func<uint, string> _symbolName;
    private readonly Func<string, MarketSession?> _session;

    public MarketSessionGate(Func<uint, string> symbolName, Func<string, MarketSession?> session)
    {
        _symbolName = symbolName;
        _session = session;
    }

    public OrderGateDecision Evaluate(in OrderCommand.Place order)
    {
        var symbol = _symbolName(order.SymbolId);
        if (_session(symbol) is { IsOpen: false } closed)
        {
            var nextOpen = closed.NextOpenUtc is { } open
                ? $"; next open {open:yyyy-MM-dd HH:mm} UTC"
                : "";
            return OrderGateDecision.Deny($"market closed for {symbol}{nextOpen}");
        }
        return OrderGateDecision.Allow;
    }
}

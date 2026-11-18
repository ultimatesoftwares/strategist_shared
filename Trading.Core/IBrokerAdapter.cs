namespace Trading.Core;

/// <summary>
/// The seam where real broker SDKs plug in (Interactive Brokers, Alpaca,
/// cTrader, Binance, etc.). One adapter per broker; the host wires adapter
/// events into the engine and routes engine commands back out.
///
/// Implementations raise <see cref="MarketData"/> and <see cref="Execution"/>
/// on whatever thread the SDK uses — the host is responsible for marshalling
/// onto the engine thread.
/// </summary>
public interface IBrokerAdapter : IDisposable
{
    string Name { get; }

    event Action<BookUpdate>? MarketData;
    event Action<ExecutionReport>? Execution;

    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Subscribe the broker's feed for a symbol, mapping it to the engine's id.</summary>
    Task SubscribeAsync(string brokerSymbol, uint engineSymbolId, CancellationToken ct = default);

    void PlaceOrder(OrderCommand.Place order);
    void CancelOrder(OrderCommand.Cancel cancel);
}

/// <summary>
/// Optional broker capability: closed OHLC bars for subscribed symbols
/// (feeding bar-driven strategies, e.g. <see cref="StrategyKind.Onnx"/>).
/// Adapters without a native bar feed can aggregate bars from their tick
/// stream. Raised on whatever thread the SDK uses — the host marshals onto
/// the engine thread.
/// </summary>
public interface IBarSource
{
    event Action<Bar>? Bars;
}

/// <summary>
/// Optional broker capability: the human-readable refusal behind a rejected
/// order. The FFI-blittable <see cref="ExecutionReport"/> cannot carry a
/// string, so adapters stash the reason here immediately before raising the
/// Rejected report; because <see cref="IBrokerAdapter.Execution"/> handlers
/// run inline on the raising thread, a consumer reading the reason from
/// inside its handler always observes it.
/// </summary>
public interface IRejectReasonSource
{
    /// <summary>The stashed reason for an order's rejection, consuming it;
    /// null when the broker gave none (or it was already taken).</summary>
    string? TakeRejectReason(ulong orderId);
}

/// <summary>
/// Backing store for <see cref="IRejectReasonSource"/>. Bounded: rejects are
/// rare, but when nothing consumes the reasons (e.g. the order log is
/// disabled) the buffer resets rather than grow for the life of the session.
/// </summary>
public sealed class RejectReasonBuffer : IRejectReasonSource
{
    private const int MaxEntries = 1024;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, string> _reasons = new();

    public void Put(ulong orderId, string reason)
    {
        if (_reasons.Count >= MaxEntries)
            _reasons.Clear();
        _reasons[orderId] = reason;
    }

    public string? TakeRejectReason(ulong orderId) =>
        _reasons.TryRemove(orderId, out var reason) ? reason : null;
}

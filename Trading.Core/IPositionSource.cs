namespace Trading.Core;

/// <summary>
/// Optional broker capability: report the account's actual net positions so
/// the host can reconcile the engine's fill-derived position against broker
/// truth.
///
/// The engine's position is pure fill accounting over executions it can
/// attribute to its own orders; it drifts whenever a fill event is lost or
/// unattributable, or a position changes outside the engine (manual close in
/// the broker UI, SL/TP, stop-out). Adapters that can query open positions
/// implement this; <see cref="EngineHost.SyncPositionsAsync"/> applies the
/// correction on the engine thread.
/// </summary>
public interface IPositionSource
{
    /// <summary>
    /// Net open position per broker symbol name, in signed engine lots
    /// (positive = long). Symbols with no open position may be omitted.
    /// Symbol names match the ones passed to
    /// <see cref="IBrokerAdapter.SubscribeAsync"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, long>> GetPositionsAsync(CancellationToken ct = default);
}

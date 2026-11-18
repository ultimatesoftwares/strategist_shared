namespace Trading.Core;

/// <summary>
/// Session state of one symbol's market: whether it is currently open for
/// trading and, when known, the next transition times. Transition times are
/// best-effort (schedules and clocks are cached) and may be null when the
/// venue trades continuously (crypto) or the source cannot compute them.
/// </summary>
public readonly record struct MarketSession(bool IsOpen, DateTime? NextOpenUtc, DateTime? NextCloseUtc);

/// <summary>
/// Optional broker capability: report per-symbol market session state so the
/// host can block strategy placements while the market is closed and surface
/// open/closed status over REST.
///
/// Implementations must be cheap and non-blocking — this is called on the
/// engine thread for every gated order — so they compute from cached
/// schedules/clocks rather than hitting the network.
/// </summary>
public interface IMarketSessionSource
{
    /// <summary>
    /// Session state for a subscribed symbol (the name passed to
    /// <see cref="IBrokerAdapter.SubscribeAsync"/>), or null when unknown
    /// (no schedule available — callers must treat unknown as open).
    /// </summary>
    MarketSession? GetSession(string symbol);
}

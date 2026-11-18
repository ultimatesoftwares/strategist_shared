namespace Trading.Core;

/// <summary>
/// Optional broker capability: replay a symbol's historical quotes as the
/// same <see cref="BookUpdate"/> stream the live feed produces, so recorded
/// history is directly consumable by the backtest engine (same CSV format,
/// same touch-level synthesis, same per-adapter price scale).
///
/// The symbol does not need to be subscribed for live data; adapters resolve
/// it through their own symbol maps (populated at connect time). Updates are
/// delivered in ascending timestamp order — backtest replay requires
/// monotonic time.
/// </summary>
public interface IHistoricalDataSource
{
    /// <summary>
    /// Fetch historical quotes for <paramref name="symbol"/> over
    /// [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) and emit them as
    /// touch-level book updates stamped with <paramref name="symbolId"/>.
    /// The callback is invoked sequentially from the fetching task (never
    /// concurrently), so a non-thread-safe sink such as
    /// <see cref="MarketDataRecorder"/> is a valid consumer.
    /// Returns the number of updates emitted.
    /// </summary>
    Task<long> FetchHistoryAsync(
        string symbol,
        uint symbolId,
        DateTime fromUtc,
        DateTime toUtc,
        Action<BookUpdate> onUpdate,
        CancellationToken ct = default);
}

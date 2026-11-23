namespace LSE.NET;

/// <summary>A half-open time window [From, To). Request parameters are converted to UTC.</summary>
public readonly record struct LseDateRange(DateTimeOffset From, DateTimeOffset To);

/// <summary>One instrument of the LSE catalogue, as /v3/pairs lists it.</summary>
/// <param name="Market">The market folder, e.g. "us_stocks", "forex", "crypto".</param>
/// <param name="Symbol">The catalogue spelling, e.g. "AAPL", "EUR_USD", "BTCUSDT".</param>
public sealed record LsePair(string Market, string Symbol, long PairId, string Name);

/// <summary>One OHLCV candle of an LSE export (UTC bucket start).</summary>
public readonly record struct LseCandle(
    DateTime Time, double Open, double High, double Low, double Close,
    double Volume, long Trades);

/// <summary>One print of an LSE trade slice (timeframe=tick): time, price,
/// size and kind, UTC with nanosecond precision (truncated to .NET ticks).</summary>
public readonly record struct LseTrade(DateTime Time, double Price, double Size, string Kind);

/// <summary>A downloaded export: the whole file, its name, the rows the
/// server charged (-1 when it did not say) and whether the server's SHA-256
/// was present and verified.</summary>
public sealed record LseExport(
    byte[] Bytes, string FileName, long Rows, string Format, bool ChecksumVerified)
{
    /// <summary>Vendor export identifier, when present.</summary>
    public string? ExportId { get; init; }
    /// <summary>Whether the server reports split-adjusted data, when present.</summary>
    public bool? Adjusted { get; init; }
}

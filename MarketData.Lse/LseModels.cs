namespace MarketData.Lse;

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
    byte[] Bytes, string FileName, long Rows, string Format, bool ChecksumVerified);

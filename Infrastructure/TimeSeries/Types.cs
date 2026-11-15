// Venue-neutral time-series contracts: the market-data value types and the
// history-source port. Implementations (QuestDB store, venue fetchers) live
// in Strategist.Infrastructure.TimeSeries.

using Strategist.Infrastructure.TimeSeries;

namespace Strategist.TimeSeries;

/// <summary>A UTC half-open window [From, To) of historical data.</summary>
public readonly record struct DateRange(DateTime From, DateTime To)
{
    public TimeSpan Length => To - From;
    public override string ToString() => $"{From:yyyy-MM-ddTHH:mm:ssZ}..{To:yyyy-MM-ddTHH:mm:ssZ}";
}

/// <summary>Calendar unit used to split a range into backtest-friendly chunks.</summary>
public enum RangeUnit { Day, Week, Month, Year }

/// <summary>One OHLCV bar of the `bars` table (schema shared with the
/// server's historical provisioning: symbol + tf tags, ts designated).</summary>
public readonly record struct Bar(
    string Symbol, string Timeframe, DateTime Ts,
    double Open, double High, double Low, double Close, double Volume);

/// <summary>One quote tick of the `ticks` table.</summary>
public readonly record struct Tick(
    string Symbol, string Venue, DateTime Ts,
    double Bid, double Ask, double Last, double LastSize, long Seq);

/// <summary>
/// A venue that can serve historical market data to the QuestDB store.
/// The hosting process implements this over its broker connectivity
/// (cTrader OpenAPI, the fleet hub's MT history REST, Alpaca REST) and
/// registers it with
/// <see cref="QuestDb.RegisterSource"/>.
/// </summary>
public interface IVenueHistorySource
{
    /// <summary>Venue name: "ctrader", "metatrader", "alpaca" or "databento".</summary>
    string Venue { get; }

    /// <summary>Bars of [range.From, range.To) from the venue, oldest first.</summary>
    Task<IReadOnlyList<Bar>> FetchBarsAsync(
        string symbol, string timeframe, DateRange range, CancellationToken ct = default);

    /// <summary>Ticks of [range.From, range.To) from the venue, oldest first.</summary>
    Task<IReadOnlyList<Tick>> FetchTicksAsync(
        string symbol, DateRange range, CancellationToken ct = default);
}

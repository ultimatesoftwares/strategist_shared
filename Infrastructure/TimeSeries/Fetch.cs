using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

// Venue-backed fetching: pull history from a registered broker source
// (ctrader, metatrader, alpaca) into the store.
public sealed partial class QuestDb
{
    /// <summary>Venues that can back the historical store.</summary>
    public static readonly IReadOnlyList<string> FetchVenues =
        ["ctrader", "metatrader", "alpaca"];

    /// <summary>Registers the host's history source for one of
    /// <see cref="FetchVenues"/>.</summary>
    public void RegisterSource(IVenueHistorySource source)
    {
        if (!FetchVenues.Contains(source.Venue, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"'{source.Venue}' cannot back the historical store — supported venues: " +
                $"{string.Join(", ", FetchVenues)}.",
                nameof(source));
        _sources[source.Venue] = source;
    }

    /// <summary>
    /// Fetches bars from a venue ("ctrader", "metatrader" or "alpaca")
    /// into the store as a fault-tolerant job:
    /// the range is split into month microtasks, each retried independently
    /// and throttled between calls (rate limits), with state published to the
    /// bus. A failed chunk is skipped and reported in the result, never fatal.
    /// </summary>
    public async Task<FetchJobResult> FetchBarsAsync(
        string venue, string symbol, string timeframe, DateRange range,
        CancellationToken ct = default, string? jobId = null)
    {
        var source = Source(venue);
        var result = await RunFetchJobAsync("bars", venue, symbol, timeframe,
            range, RangeUnit.Month, jobId,
            async (chunk, token) => await WriteBarsAsync(
                await source.FetchBarsAsync(symbol, timeframe, chunk, token), token),
            ct);
        _logger.LogInformation("questdb: fetched {Count} {Tf} bars of {Symbol} from {Venue} ({Range})",
            result.Written, timeframe, symbol, venue, range);
        return result;
    }

    /// <summary>Fetches ticks from a venue into the store as a fault-tolerant
    /// job chunked by day (a day is the practical cap of one tick call),
    /// retried, throttled and state-published like <see cref="FetchBarsAsync"/>.</summary>
    public async Task<FetchJobResult> FetchTicksAsync(
        string venue, string symbol, DateRange range, CancellationToken ct = default,
        string? jobId = null)
    {
        var source = Source(venue);
        var result = await RunFetchJobAsync("ticks", venue, symbol, null,
            range, RangeUnit.Day, jobId,
            async (chunk, token) => await WriteTicksAsync(
                await source.FetchTicksAsync(symbol, chunk, token), token),
            ct);
        _logger.LogInformation("questdb: fetched {Count} ticks of {Symbol} from {Venue} ({Range})",
            result.Written, symbol, venue, range);
        return result;
    }

    /// <summary>Bars of a range, read from the store — any part not yet
    /// stored is first fetched from the venue. The backtest entry point:
    /// one call guarantees coverage, then serves from QuestDB.</summary>
    public async Task<List<Bar>> GetOrFetchBarsAsync(
        string venue, string symbol, string timeframe, DateRange range,
        CancellationToken ct = default)
    {
        var coverage = await BarCoverageAsync(symbol, timeframe, ct);
        if (coverage is null)
        {
            await FetchBarsAsync(venue, symbol, timeframe, range, ct);
        }
        else
        {
            if (range.From < coverage.Value.From)
                await FetchBarsAsync(venue, symbol, timeframe,
                    new DateRange(range.From, coverage.Value.From), ct);
            if (range.To > coverage.Value.To)
                await FetchBarsAsync(venue, symbol, timeframe,
                    new DateRange(coverage.Value.To, range.To), ct);
        }
        return await ReadBarsAsync(symbol, timeframe, range, ct);
    }

    /// <summary>Tick-level counterpart of <see cref="GetOrFetchBarsAsync"/>:
    /// fetches the uncovered head/tail of the range from the venue, then
    /// serves the whole window from the store.</summary>
    public async Task<List<Tick>> GetOrFetchTicksAsync(
        string venue, string symbol, DateRange range, CancellationToken ct = default)
    {
        var coverage = await TickCoverageAsync(symbol, ct);
        if (coverage is null)
        {
            await FetchTicksAsync(venue, symbol, range, ct);
        }
        else
        {
            if (range.From < coverage.Value.From)
                await FetchTicksAsync(venue, symbol,
                    new DateRange(range.From, coverage.Value.From), ct);
            if (range.To > coverage.Value.To)
                await FetchTicksAsync(venue, symbol,
                    new DateRange(coverage.Value.To, range.To), ct);
        }
        return await ReadTicksAsync(symbol, range, venue: null, ct);
    }

    private IVenueHistorySource Source(string venue)
    {
        if (!FetchVenues.Contains(venue, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"unknown venue '{venue}' — supported: {string.Join(", ", FetchVenues)}.", nameof(venue));
        return _sources.TryGetValue(venue, out var source)
            ? source
            : throw new InvalidOperationException(
                $"no history source registered for '{venue}' — the hosting process must call RegisterSource first.");
    }
}

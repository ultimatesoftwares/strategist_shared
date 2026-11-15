using System.Text.Json;

using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

// Reads: /exec SQL out of the bars/ticks tables — ranged, grouped, coverage.
public sealed partial class QuestDb
{
    /// <summary>Bars of one symbol/timeframe inside [range.From, range.To), oldest first.</summary>
    public async Task<List<Bar>> ReadBarsAsync(
        string symbol, string timeframe, DateRange range, CancellationToken ct = default)
    {
        var rows = await QueryAsync(
            "select ts, open, high, low, close, volume from bars" +
            $" where symbol = '{Escape(symbol)}' and tf = '{Escape(timeframe)}'" +
            $" and ts >= '{Iso(range.From)}' and ts < '{Iso(range.To)}' order by ts",
            ct);
        return [.. rows.Select(r => new Bar(symbol, timeframe, Ts(r[0]),
            r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble(), r[4].GetDouble(), r[5].GetDouble()))];
    }

    /// <summary>Ticks of one symbol inside [range.From, range.To), oldest first;
    /// optionally restricted to one venue.</summary>
    public async Task<List<Tick>> ReadTicksAsync(
        string symbol, DateRange range, string? venue = null, CancellationToken ct = default)
    {
        var venueFilter = venue is null ? "" : $" and venue = '{Escape(venue)}'";
        var rows = await QueryAsync(
            "select ts, venue, bid, ask, last, last_size, seq from ticks" +
            $" where symbol = '{Escape(symbol)}'{venueFilter}" +
            $" and ts >= '{Iso(range.From)}' and ts < '{Iso(range.To)}' order by ts",
            ct);
        return [.. rows.Select(r => new Tick(symbol, r[1].GetString() ?? "", Ts(r[0]),
            r[2].GetDouble(), r[3].GetDouble(), r[4].GetDouble(), r[5].GetDouble(), r[6].GetInt64()))];
    }

    /// <summary>
    /// Bars grouped into calendar chunks (e.g. one list per year) — the shape
    /// backtests iterate: each chunk is fetched with its own query, so a
    /// multi-year run never materialises the full history at once.
    /// </summary>
    public async Task<List<(DateRange Range, List<Bar> Bars)>> ReadBarsGroupedAsync(
        string symbol, string timeframe, DateRange range, RangeUnit unit,
        CancellationToken ct = default)
    {
        var groups = new List<(DateRange, List<Bar>)>();
        foreach (var chunk in SplitRange(range, unit))
            groups.Add((chunk, await ReadBarsAsync(symbol, timeframe, chunk, ct)));
        return groups;
    }

    /// <summary>Tick-level counterpart of <see cref="ReadBarsGroupedAsync"/>:
    /// ticks grouped into calendar chunks, one query per chunk.</summary>
    public async Task<List<(DateRange Range, List<Tick> Ticks)>> ReadTicksGroupedAsync(
        string symbol, DateRange range, RangeUnit unit, string? venue = null,
        CancellationToken ct = default)
    {
        var groups = new List<(DateRange, List<Tick>)>();
        foreach (var chunk in SplitRange(range, unit))
            groups.Add((chunk, await ReadTicksAsync(symbol, chunk, venue, ct)));
        return groups;
    }

    /// <summary>Stored coverage of a symbol/timeframe (min..max ts), or null
    /// when nothing is stored — lets a backtest clamp its requested range.</summary>
    public async Task<DateRange?> BarCoverageAsync(
        string symbol, string timeframe, CancellationToken ct = default)
    {
        var rows = await QueryAsync(
            "select min(ts), max(ts) from bars" +
            $" where symbol = '{Escape(symbol)}' and tf = '{Escape(timeframe)}'", ct);
        if (rows.Count == 0 || rows[0][0].ValueKind == JsonValueKind.Null) return null;
        return new DateRange(Ts(rows[0][0]), Ts(rows[0][1]));
    }

    /// <summary>Stored tick coverage of a symbol (min..max ts), or null when
    /// nothing is stored; optionally restricted to one venue.</summary>
    public async Task<DateRange?> TickCoverageAsync(
        string symbol, CancellationToken ct = default, string? venue = null)
    {
        var venueFilter = venue is null ? "" : $" and venue = '{Escape(venue)}'";
        var rows = await QueryAsync(
            "select min(ts), max(ts) from ticks" +
            $" where symbol = '{Escape(symbol)}'{venueFilter}", ct);
        if (rows.Count == 0 || rows[0][0].ValueKind == JsonValueKind.Null) return null;
        return new DateRange(Ts(rows[0][0]), Ts(rows[0][1]));
    }

    /// <summary>Raw /exec query; rows of JSON cells. Empty when the table does
    /// not exist yet (first run before any write).</summary>
    public async Task<List<JsonElement[]>> QueryAsync(string sql, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            $"{_baseUrl}/exec?query={Uri.EscapeDataString(sql)}", ct);
        var rows = new List<JsonElement[]>();
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // "table does not exist" is a normal empty store, not an error.
            if (body.Contains("does not exist", StringComparison.OrdinalIgnoreCase)) return rows;
            throw new InvalidOperationException($"QuestDB query failed ({(int)response.StatusCode}): {body}");
        }
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        if (!doc.TryGetProperty("dataset", out var dataset)) return rows;
        foreach (var row in dataset.EnumerateArray())
            rows.Add([.. row.EnumerateArray()]);
        return rows;
    }
}

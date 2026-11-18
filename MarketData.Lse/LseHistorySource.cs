using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Strategist.TimeSeries;

namespace MarketData.Lse;

/// <summary>
/// The LSE Data 3.0 history source: serves bars and ticks to the QuestDB
/// store (venue tag "lse") from londonstrategicedge.com. Symbols are
/// resolved against the vendor's catalogue once and cached — platform
/// spellings ("EURUSD", "EUR/USD", "BTCUSD") find their catalogue pair
/// ("EUR_USD", "BTCUSDT"; crypto is quoted in Tether). Ticks are the trade
/// slice (prints), so bid and ask carry the trade price; quote-priced
/// markets (forex, commodities) serve candles only.
/// </summary>
public sealed class LseHistorySource : IVenueHistorySource
{
    public const string VenueName = "lse";

    private static readonly Dictionary<string, string> Timeframes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Platform (MT-style) spellings.
        ["S1"] = "1s", ["S5"] = "5s", ["S15"] = "15s", ["S30"] = "30s",
        ["M1"] = "1m", ["M3"] = "3m", ["M5"] = "5m", ["M15"] = "15m", ["M30"] = "30m",
        ["H1"] = "1h", ["H4"] = "4h", ["D1"] = "1d", ["W1"] = "1w", ["MN"] = "1mo", ["MN1"] = "1mo",
        // The server's own spellings pass through.
        ["tick"] = "tick", ["1s"] = "1s", ["5s"] = "5s", ["15s"] = "15s", ["30s"] = "30s",
        ["1m"] = "1m", ["3m"] = "3m", ["5m"] = "5m", ["15m"] = "15m", ["30m"] = "30m",
        ["1h"] = "1h", ["4h"] = "4h", ["1d"] = "1d", ["1w"] = "1w", ["1mo"] = "1mo",
    };

    private readonly LseDataClient _client;
    private readonly ILogger<LseHistorySource>? _log;
    private readonly ConcurrentDictionary<string, LsePair> _pairs =
        new(StringComparer.OrdinalIgnoreCase);

    public LseHistorySource(LseDataClient client, ILogger<LseHistorySource>? log = null)
    {
        _client = client;
        _log = log;
    }

    public string Venue => VenueName;

    public async Task<IReadOnlyList<Bar>> FetchBarsAsync(
        string symbol, string timeframe, DateRange range, CancellationToken ct = default)
    {
        var pair = await ResolveAsync(symbol, ct);
        var candles = await _client.GetCandlesAsync(
            pair.Market, pair.Symbol, MapTimeframe(timeframe), range, ct);
        var bars = new List<Bar>(candles.Count);
        foreach (var c in candles)
            bars.Add(new Bar(symbol, timeframe, c.Time, c.Open, c.High, c.Low, c.Close, c.Volume));
        return bars;
    }

    public async Task<IReadOnlyList<Tick>> FetchTicksAsync(
        string symbol, DateRange range, CancellationToken ct = default)
    {
        var pair = await ResolveAsync(symbol, ct);
        var trades = await _client.GetTradesAsync(pair.Market, pair.Symbol, range, ct);
        var ticks = new List<Tick>(trades.Count);
        // The slice carries prints only; seq orders same-timestamp trades
        // deterministically so re-fetching a chunk dedups cleanly.
        long seq = 0;
        foreach (var t in trades)
            ticks.Add(new Tick(symbol, VenueName, t.Time, t.Price, t.Price, t.Price, t.Size, seq++));
        return ticks;
    }

    /// <summary>The platform timeframe as the server spells it, e.g. "M1" to
    /// "1m"; the server's own spellings pass through.</summary>
    internal static string MapTimeframe(string timeframe) =>
        Timeframes.TryGetValue(timeframe.Trim(), out var mapped)
            ? mapped
            : throw new ArgumentException(
                $"timeframe '{timeframe}' is not served by LSE (use {string.Join(", ", Timeframes.Keys)})",
                nameof(timeframe));

    /// <summary>Catalogue spellings a platform symbol may resolve to, in the
    /// order to try them: as given, '/' as '_', compacted, a compact forex
    /// pair split as the catalogue spells it ("EURUSD" to "EUR_USD"), and
    /// the Tether quote for crypto ("BTC/USD" and "BTCUSD" to "BTCUSDT").</summary>
    internal static IReadOnlyList<string> SymbolCandidates(string symbol)
    {
        symbol = symbol.Trim();
        var candidates = new List<string> { symbol };
        void Add(string candidate)
        {
            if (!candidates.Any(c => c.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(candidate);
        }
        Add(symbol.Replace('/', '_'));
        var compact = symbol.Replace("/", "").Replace("_", "");
        Add(compact);
        if (compact.Length == 6 && compact.All(char.IsAsciiLetter))
            Add(compact[..3] + "_" + compact[3..]);
        if (compact.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            Add(compact + "T");
        return candidates;
    }

    private async Task<LsePair> ResolveAsync(string symbol, CancellationToken ct)
    {
        if (_pairs.TryGetValue(symbol, out var cached))
            return cached;
        var candidates = SymbolCandidates(symbol);
        foreach (var candidate in candidates)
        {
            var pair = await _client.FindPairAsync(candidate, ct: ct);
            if (pair is null)
                continue;
            _log?.LogInformation(
                "lse: resolved '{Symbol}' to {Market}/{Pair} (pair {PairId})",
                symbol, pair.Market, pair.Symbol, pair.PairId);
            _pairs[symbol] = pair;
            return pair;
        }
        throw new LseException(404, "symbol-unknown",
            $"'{symbol}' is not in the LSE catalogue (tried {string.Join(", ", candidates)})");
    }
}

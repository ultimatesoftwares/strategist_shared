using System.Text.Json;

namespace LSE.NET;

public sealed partial class LseClient
{
    /// <summary>Reads candles by symbol, resolving the market unless specified.</summary>
    public async Task<IReadOnlyList<LseCandle>> CandlesAsync(string symbol, string timeframe = "1m",
        DateOnly? start = null, DateOnly? end = null, int limit = 5000, string order = "asc",
        string? market = null, bool? adjusted = null, CancellationToken ct = default)
    {
        ValidateOrder(order, limit);
        var latestRequested = start is null;
        var selection = await ResolveAsync(symbol, market, timeframe, ct).ConfigureAwait(false);
        var last = end ?? DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<LseCandle> candles;
        if (start is null)
        {
            // Overview supplies the latest archived day even when a market is closed.
            if (end is null)
            {
                using var overview = await OverviewAsync(selection.Market, selection.Symbol, ct: ct).ConfigureAwait(false);
                if (overview.RootElement.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Object
                    && Text(history, "last") is { Length: >= 10 } latest
                    && DateOnly.TryParseExact(latest[..10], "yyyy-MM-dd", out var day))
                    last = day;
            }
            var lookback = (int)Math.Clamp(Math.Ceiling(limit * FrameMinutes(timeframe) / 390d) * 2 + 7, 7, 3650);
            start = last.AddDays(-Math.Min(last.DayNumber, lookback));
        }
        try
        {
            var file = await Http.ExportAsync(selection with
                { FromDate = start, ToDate = last, Format = "csv", Adjusted = adjusted }, ct).ConfigureAwait(false);
            candles = LseParsers.ParseCandlesCsv(file.Bytes, ct);
        }
        catch (LseException e) when (IsEmptySelection(e)) { return []; }
        return (order == "desc" ? candles.Reverse() : latestRequested ? candles.TakeLast(limit) : candles)
            .Take(limit).ToArray();
    }

    /// <summary>Saves history by symbol without requiring the market folder.</summary>
    public async Task<string> HistoryAsync(string symbol, DateOnly start, DateOnly end, string timeframe = "1m",
        string? market = null, string format = "parquet", string? destinationDirectory = null,
        CancellationToken ct = default)
    {
        var selection = await ResolveAsync(symbol, market, timeframe, ct).ConfigureAwait(false);
        return await ExportAsync(selection with { FromDate = start, ToDate = end, Format = format },
            destinationDirectory, ct).ConfigureAwait(false);
    }

    /// <summary>Reads candles by symbol for a half-open timestamp window.</summary>
    public async Task<IReadOnlyList<LseCandle>> CandlesAsync(string symbol, LseDateRange window,
        string timeframe = "1m", int limit = 5000, string order = "asc", string? market = null,
        CancellationToken ct = default)
    {
        ValidateOrder(order, limit);
        var selection = await ResolveAsync(symbol, market, timeframe, ct).ConfigureAwait(false);
        var candles = await Http.GetCandlesAsync(selection.Market, selection.Symbol!, timeframe, window, ct).ConfigureAwait(false);
        return (order == "desc" ? candles.Reverse() : candles).Take(limit).ToArray();
    }

    /// <summary>Saves history by symbol for a half-open timestamp window.</summary>
    public async Task<string> HistoryAsync(string symbol, LseDateRange window, string timeframe = "1m",
        string? market = null, string format = "parquet", string? destinationDirectory = null,
        CancellationToken ct = default)
    {
        var selection = await ResolveAsync(symbol, market, timeframe, ct).ConfigureAwait(false);
        return await ExportAsync(selection with { Window = window, Format = format },
            destinationDirectory, ct).ConfigureAwait(false);
    }

    /// <summary>Reads candles by OCC ticker or by root, strike, expiry and right.</summary>
    public Task<IReadOnlyList<LseCandle>> OptionCandlesAsync(string contract, decimal? strike = null,
        DateOnly? expiry = null, string? type = null, DateOnly? start = null, DateOnly? end = null,
        string timeframe = "1m", int limit = 5000, string order = "asc", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        var ticker = contract.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (strike is not null || expiry is not null || type is not null)
        {
            if (ticker.Length is < 1 or > 6 || expiry is null || strike is null || strike is < 0 or > 99999.999m
                || strike * 1000 != decimal.Truncate(strike.Value * 1000)
                || type?.ToLowerInvariant() is not ("call" or "put"))
                throw new ArgumentException("Supply a root, valid strike, expiry, and call or put.");
            ticker = FormattableString.Invariant($"{ticker}{expiry:yyMMdd}{char.ToUpperInvariant(type[0])}{strike * 1000:00000000}");
        }
        if (ticker.Length is < 16 or > 21 || ticker[^9] is not ('C' or 'P')
            || !ticker[^15..^9].All(char.IsAsciiDigit) || !ticker[^8..].All(char.IsAsciiDigit))
            throw new ArgumentException("Expected an OCC option ticker.", nameof(contract));
        var last = end ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return CandlesAsync(ticker[..^15].PadRight(6) + ticker[^15..], timeframe, start ?? last.AddDays(-30),
            last, limit, order, "us_options", ct: ct);
    }

    /// <summary>Lists instruments or grouped contract underlyings in a market category.</summary>
    public async Task<IReadOnlyList<JsonElement>> CatalogAsync(string? category = null, CancellationToken ct = default)
    {
        if (category is not null)
            return await CatalogMarketAsync(NormalizeMarket(category), ct).ConfigureAwait(false);
        var rows = new List<JsonElement>();
        foreach (var market in await MarketsAsync(ct).ConfigureAwait(false))
            if (Text(market, "folder") is { } folder)
                rows.AddRange(await CatalogMarketAsync(folder, ct).ConfigureAwait(false));
        return rows;
    }

    /// <summary>Lists archived instruments and their native coverage fields.</summary>
    public Task<IReadOnlyList<JsonElement>> DatasetsAsync(string? dataset = null, CancellationToken ct = default) =>
        CatalogAsync(dataset, ct);

    private Task<IReadOnlyList<JsonElement>> CatalogMarketAsync(string market, CancellationToken ct) =>
        PairsAsync(new() { Market = market, GroupBy = market is "cme" or "us_options" ? "underlying" : null }, ct);

    private async Task<LseMarketSelection> ResolveAsync(string symbol, string? market, string timeframe, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);
        symbol = symbol.Trim();
        if (market is not null)
            return new(NormalizeMarket(market)) { Symbol = symbol, Timeframe = timeframe };
        var compact = symbol.Replace("/", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        var candidates = new[] { symbol, symbol.Replace('/', '_'), compact,
            compact.Length == 6 ? compact.Insert(3, "_") : compact,
            compact.EndsWith("USD", StringComparison.OrdinalIgnoreCase) ? compact + "T" : compact };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (await Http.FindPairAsync(candidate, ct: ct).ConfigureAwait(false) is { } pair)
                return new(pair.Market) { Symbol = pair.Symbol, Timeframe = timeframe };
        throw new LseException(404, "symbol-unknown", "The symbol was not found in the LSE catalogue.");
    }

    private static string NormalizeMarket(string market) => market.Trim().ToLowerInvariant() switch
    {
        "stock" or "stocks" or "equity" or "equities" or "etf" or "etfs" => "us_stocks",
        "option" or "options" or "opra" => "us_options",
        "fx" => "forex", "index" => "indices", "future" => "futures", "commodity" => "commodities",
        "totalview" => "nasdaq", var folder => folder
    };

    private static string? Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var item)
        && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static void ValidateOrder(string order, int limit)
    {
        if (order is not ("asc" or "desc"))
            throw new ArgumentException("Expected asc or desc.", nameof(order));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
    }

    private static double FrameMinutes(string timeframe)
    {
        if (timeframe == "1mo") return 30 * 1440;
        if (timeframe.Length > 1 && int.TryParse(timeframe.AsSpan(0, timeframe.Length - 1), out var count) && count > 0)
            return timeframe[^1] switch { 's' => count / 60d, 'h' => count * 60d, 'd' => count * 1440d, 'w' => count * 10080d, _ => count };
        return 1;
    }
}

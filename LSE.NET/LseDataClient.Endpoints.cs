using System.Globalization;
using System.Text.Json;

namespace LSE.NET;

public sealed partial class LseDataClient
{
    /// <summary>Searches pairs with cursor paging and exchange contract filters.</summary>
    public Task<JsonDocument> SearchPairsAsync(LsePairSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Limit is < 0 or > 10000 || search.Offset is < 0)
            throw new ArgumentOutOfRangeException(nameof(search), "Expected a limit between 0 and 10000 and a nonnegative offset.");
        return Json("/v3/pairs", ct, ("market", search.Market), ("symbol", search.Symbol),
            ("q", search.Query), ("limit", Wire(search.Limit)), ("offset", Wire(search.Offset)),
            ("sort", search.Sort), ("cursor", search.Cursor), ("group_by", search.GroupBy),
            ("underlying", search.Underlying), ("product", search.Product), ("expiry", Day(search.Expiry)),
            ("instrument_type", search.InstrumentType), ("option_type", search.OptionType));
    }

    /// <summary>Lists archive markets, pairs in one market, or the largest archives.</summary>
    public Task<JsonDocument> GetCatalogueAsync(string? market = null, string? query = null,
        int? limit = null, int? offset = null, int? largest = null, CancellationToken ct = default) =>
        Json("/v3/catalogue", ct, ("market", market), ("q", query), ("limit", Wire(limit)),
            ("offset", Wire(offset)), ("largest", Wire(largest)));

    /// <summary>Gets split and dividend factors by symbol or pair ID.</summary>
    public Task<JsonDocument> GetFactorsAsync(string market, string? symbol = null, long? pairId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        if (string.IsNullOrWhiteSpace(symbol) && pairId is null)
            throw new ArgumentException("Supply a symbol or pair ID.");
        return Json("/v3/factors", ct, ("market", market), ("symbol", symbol), ("pair_id", Wire(pairId)));
    }

    /// <summary>Lists the stored tables of a market.</summary>
    public Task<JsonDocument> GetTablesAsync(string market, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        return Json("/v3/tables", ct, ("market", market));
    }

    /// <summary>Gets archive statistics, optionally including one instrument's minute history.</summary>
    public Task<JsonDocument> GetOverviewAsync(string? market = null, string? symbol = null,
        long? pairId = null, CancellationToken ct = default) =>
        Json("/v3/overview", ct, ("market", market), ("symbol", symbol), ("pair_id", Wire(pairId)));

    /// <summary>Downloads a catalogue image without taking an export slot. Pass its origin-relative photo link.</summary>
    public async Task<LseInstrumentImage> GetInstrumentImageAsync(string photoPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoPath);
        const string prefix = "/v3/photo/";
        var path = photoPath.Split('?', 2)[0];
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || path[prefix.Length..].Split('/') is not [var kind, var key, "light" or "dark"]
            || !PhotoSegment(kind) || !PhotoSegment(key))
            throw new ArgumentException("Expected /v3/photo/{kind}/{key}/{light|dark}.", nameof(photoPath));
        using var lifetime = Link(ct);
        return await ExecuteAsync(photoPath, async (response, token) => new LseInstrumentImage(
            await ReadBodyAsync(response.Content, _options.MaxJsonBytes, token).ConfigureAwait(false),
            response.Content.Headers.ContentType?.MediaType, response.Headers.ETag?.ToString(),
            response.Headers.CacheControl?.ToString()), lifetime.Token, prefix).ConfigureAwait(false);
    }

    /// <summary>Previews candles, Greeks, or exchange data for a market selection.</summary>
    public Task<JsonDocument> GetPreviewAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        Json("/v3/preview", ct, WithFormat(MarketFields(selection, requireDates: false), selection.Format));

    /// <summary>Estimates a market selection, including pair-ID and option-underlying exports.</summary>
    public Task<JsonDocument> GetEstimateAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        Json("/v3/estimate", ct, WithFormat(MarketFields(selection, requireDates: false), selection.Format));

    /// <summary>Exports candles, trades, quotes, books, orders, or Greeks as CSV or Parquet.</summary>
    public Task<LseExport> ExportAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        ExportMarketAsync("/v3/export", selection, ct);

    /// <summary>Downloads through the vendor's /v3/candles alias for /v3/export.</summary>
    public Task<LseExport> ExportCandlesAliasAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        ExportMarketAsync("/v3/candles", selection, ct);

    private Task<LseExport> ExportMarketAsync(string path, LseMarketSelection selection, CancellationToken ct,
        string? fallbackName = null)
    {
        var fields = MarketFields(selection, requireDates: true);
        return Download(path, selection.Format, ct, fields, fallbackName);
    }

    /// <summary>Downloads through the legacy /v3/slice route. New callers should use ExportAsync.</summary>
    public Task<LseExport> ExportSliceAsync(LseMarketSelection selection, string table, int? barSeconds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        var fields = MarketFields(selection, requireDates: true);
        return Download("/v3/slice", selection.Format, ct,
            [.. fields.Where(f => f.Item1 is not ("data" or "timeframe")), ("table", table), ("bar_seconds", Wire(barSeconds))]);
    }

    /// <summary>Exports a custom candle dataset with server-computed features.</summary>
    public Task<LseExport> ExportDatasetAsync(LseMarketSelection selection, LseDatasetFeatures features,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(features);
        var fields = MarketFields(selection, requireDates: true);
        return Download("/v3/dataset/export", selection.Format, ct,
            [.. fields, ("indicators", Joined(features.Indicators)), ("companion_market", features.CompanionMarket),
             ("companion_symbol", features.CompanionSymbol), ("companion_pair_id", Wire(features.CompanionPairId)),
             ("companion_features", Joined(features.CompanionFeatures)), ("corr_window", Wire(features.CorrelationWindow)),
             ("hour_start", Wire(features.HourStart)), ("hour_end", Wire(features.HourEnd)),
             ("tz", features.TimeZone), ("day_of_week", Joined(features.DaysOfWeek))]);
    }

    /// <summary>Gets a page of listed options with last prints, activity, and Greeks.</summary>
    public Task<JsonDocument> GetOptionChainAsync(LseOptionChainQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Underlying);
        return Json("/v3/options/chain", ct, ("underlying", query.Underlying), ("type", query.Type),
            ("expiry", Day(query.Expiry)), ("strike", Wire(query.Strike)), ("strike_min", Wire(query.StrikeMin)),
            ("strike_max", Wire(query.StrikeMax)), ("min_dte", Wire(query.MinDaysToExpiry)),
            ("max_dte", Wire(query.MaxDaysToExpiry)), ("limit", Wire(query.Limit)), ("offset", Wire(query.Offset)));
    }

    /// <summary>Gets a page of recent option prints filtered by time or minimum premium.</summary>
    public Task<JsonDocument> GetOptionFlowAsync(LseOptionFlowQuery? query = null, CancellationToken ct = default)
    {
        query ??= new();
        var (from, to) = Bounds(query.Window, query.FromDate, query.ToDate, required: false);
        return Json("/v3/options/flow", ct, ("underlying", query.Underlying), ("min_premium", Wire(query.MinPremium)),
            ("type", query.Type), ("expiry", Day(query.Expiry)), ("strike", Wire(query.Strike)),
            ("strike_min", Wire(query.StrikeMin)), ("strike_max", Wire(query.StrikeMax)),
            ("min_dte", Wire(query.MinDaysToExpiry)), ("max_dte", Wire(query.MaxDaysToExpiry)),
            ("from", from), ("to", to), ("order", query.Order), ("limit", Wire(query.Limit)), ("offset", Wire(query.Offset)));
    }

    /// <summary>Lists economic series or available countries and series kinds.</summary>
    public Task<JsonDocument> SearchMacroAsync(LseMacroSearch? search = null, CancellationToken ct = default)
    {
        search ??= new();
        return Json("/v3/macro", ct, ("kind", search.Kind), ("country", search.Country), ("code", search.Code),
            ("q", search.Query), ("group", search.Group), ("limit", Wire(search.Limit)), ("offset", Wire(search.Offset)));
    }

    /// <summary>Previews the latest rows of an economic series.</summary>
    public Task<JsonDocument> GetMacroPreviewAsync(LseMacroSelection selection, CancellationToken ct = default) =>
        Json("/v3/macro/preview", ct, MacroFields(selection));

    /// <summary>Gets the JSON rows of a bounded economic time series.</summary>
    public Task<JsonDocument> GetMacroSeriesAsync(LseMacroSelection selection, CancellationToken ct = default) =>
        Json("/v3/macro/series", ct, MacroFields(selection));

    /// <summary>Estimates an economic series export.</summary>
    public Task<JsonDocument> GetMacroEstimateAsync(LseMacroSelection selection, string format = "csv", CancellationToken ct = default) =>
        Json("/v3/macro/estimate", ct, WithFormat(MacroFields(selection), format));

    /// <summary>Exports observations, economic releases, or bond yields.</summary>
    public Task<LseExport> ExportMacroAsync(LseMacroSelection selection, string format = "csv", CancellationToken ct = default) =>
        Download("/v3/macro/export", format, ct, MacroFields(selection));

    /// <summary>Downloads through the vendor's /v3/macro/values alias for /v3/macro/export.</summary>
    public Task<LseExport> ExportMacroValuesAsync(LseMacroSelection selection, string format = "csv", CancellationToken ct = default) =>
        Download("/v3/macro/values", format, ct, MacroFields(selection));

    /// <summary>Exports up to six observation series as levels, year-on-year, or month-on-month values.</summary>
    public Task<LseExport> ExportMacroChartAsync(IReadOnlyList<string> codes, DateOnly from, DateOnly to,
        string mode = "level", string format = "csv", CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count is < 1 or > 6 || codes.Any(c => string.IsNullOrWhiteSpace(c) || c.Contains(','))
            || codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Count)
            throw new ArgumentException("Supply one to six distinct series codes.", nameof(codes));
        if (mode is not ("level" or "yoy" or "mom"))
            throw new ArgumentException("Expected level, yoy or mom.", nameof(mode));
        ValidateDates(from, to);
        return Download("/v3/macro/chart/export", format, ct,
            [("codes", Joined(codes)), ("mode", mode), ("from", Day(from)), ("to", Day(to))]);
    }

    /// <summary>Lists reference collections, or lists series filtered within one collection.</summary>
    public Task<JsonDocument> GetReferenceCatalogueAsync(string? dataset = null, string? symbol = null,
        string? country = null, string? query = null, int? limit = null, int? offset = null,
        CancellationToken ct = default) =>
        Json("/v3/reference", ct, ("dataset", dataset), ("symbol", symbol), ("country", country),
            ("q", query), ("limit", Wire(limit)), ("offset", Wire(offset)));

    /// <summary>Previews native rows of a reference collection.</summary>
    public Task<JsonDocument> GetReferencePreviewAsync(LseReferenceSelection selection, CancellationToken ct = default) =>
        Json("/v3/reference/preview", ct, ReferenceFields(selection));

    /// <summary>Estimates a reference collection export.</summary>
    public Task<JsonDocument> GetReferenceEstimateAsync(LseReferenceSelection selection, string format = "csv", CancellationToken ct = default) =>
        Json("/v3/reference/estimate", ct, WithFormat(ReferenceFields(selection), format));

    /// <summary>Exports a filtered reference collection (including fundamentals, earnings or credit).</summary>
    public Task<LseExport> ExportReferenceAsync(LseReferenceSelection selection, string format = "csv", CancellationToken ct = default) =>
        Download("/v3/reference/export", format, ct, ReferenceFields(selection));

    private (string, string?)[] MarketFields(LseMarketSelection selection, bool requireDates)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.Market);
        if (string.IsNullOrWhiteSpace(selection.Symbol) && selection.PairId is null
            && string.IsNullOrWhiteSpace(selection.Underlying))
            throw new ArgumentException("Supply a symbol, pair ID, or option underlying.", nameof(selection));
        var (from, to) = Bounds(selection.Window, selection.FromDate, selection.ToDate, requireDates);
        return [("market", selection.Market), ("symbol", selection.Symbol), ("pair_id", Wire(selection.PairId)),
            ("underlying", selection.Underlying), ("expiry", Day(selection.Expiry)), ("option_type", selection.OptionType),
            ("data", selection.Data), ("timeframe", selection.Timeframe), ("from", from), ("to", to),
            ("adjusted", Bit(selection.Adjusted ?? _options.Adjusted)),
            ("as_published", Bit(selection.AsPublished))];
    }

    private static (string, string?)[] MacroFields(LseMacroSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.Code);
        ValidateDates(selection.From, selection.To);
        return [("kind", selection.Kind), ("code", selection.Code), ("from", Day(selection.From)), ("to", Day(selection.To))];
    }

    private static (string, string?)[] ReferenceFields(LseReferenceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.Dataset);
        ValidateDates(selection.From, selection.To);
        return [("dataset", selection.Dataset), ("symbol", selection.Symbol), ("country", selection.Country),
            ("q", selection.Query), ("from", Day(selection.From)), ("to", Day(selection.To))];
    }

    private static (string?, string?) Bounds(LseDateRange? window, DateOnly? from, DateOnly? to, bool required)
    {
        if (window is { } range)
        {
            if (from is not null || to is not null || range.From >= range.To)
                throw new ArgumentException("Use one increasing timestamp window or calendar date bounds.");
            return (FormatTime(range.From.UtcDateTime), FormatTime(range.To.UtcDateTime));
        }
        if ((required || from is not null || to is not null) && (from is null || to is null))
            throw new ArgumentException("Both bounds are required.");
        ValidateDates(from, to);
        return (Day(from), Day(to));
    }

    private static void ValidateDates(DateOnly? from, DateOnly? to)
    {
        if (from > to)
            throw new ArgumentException("The upper date must not precede the lower date.");
    }

    private static string? Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool PhotoSegment(string segment) => segment.Length > 0 && segment is not ("." or "..")
        && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static string? Wire<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture);
    private static string? Bit(bool? value) => value is { } flag ? flag ? "1" : "0" : null;
    private static string? Joined(IReadOnlyList<string>? values) => values is null ? null : string.Join(',', values);

    private static (string, string?)[] WithFormat((string, string?)[] fields, string format)
    {
        ValidateFormat(format);
        return [.. fields, ("format", format.ToLowerInvariant())];
    }

    private Task<JsonDocument> Json(string path, CancellationToken ct, params (string, string?)[] fields) =>
        GetJsonAsync($"{path}?{Query(fields)}", ct);

    private Task<LseExport> Download(string path, string format, CancellationToken ct, (string, string?)[] fields,
        string? fallbackName = null) =>
        DownloadAsync($"{path}?{Query(WithFormat(fields, format))}", format.ToLowerInvariant(),
            fallbackName ?? $"export.{format.ToLowerInvariant()}", ct);
}

using System.Text.Json;

namespace LSE.NET;

/// <summary>One normalized macro observation, yield or release.</summary>
public sealed record LseSeriesPoint(string Symbol, string? Date, JsonElement? Value);

public sealed partial class LseClient
{
    /// <summary>Gets the macro catalogue or one filtered page. Dispose the returned document.</summary>
    public Task<JsonDocument> MacroCatalogueAsync(LseMacroSearch? search = null, CancellationToken ct = default) =>
        Http.SearchMacroAsync(search, ct);

    /// <summary>Enumerates macro catalogue entries by offset.</summary>
    public IAsyncEnumerable<JsonElement> IterMacroSeriesAsync(LseMacroSearch? search = null, int pageSize = 1000,
        CancellationToken ct = default)
    {
        ValidateCollectionPageSize(pageSize);
        search ??= new() { Kind = "observation" };
        return PagesAsync((offset, _, token) => MacroCatalogueAsync(search with
            { Kind = search.Kind ?? "observation", Limit = pageSize, Offset = offset }, token),
            "series", pageSize, search.Offset ?? 0, ct: ct);
    }

    /// <summary>Gets the native macro preview. Dispose the returned document.</summary>
    public Task<JsonDocument> MacroPreviewAsync(LseMacroSelection selection, CancellationToken ct = default) =>
        Http.GetMacroPreviewAsync(selection, ct);

    /// <summary>Gets native macro series rows and metadata. Dispose the returned document.</summary>
    public Task<JsonDocument> MacroSeriesAsync(LseMacroSelection selection, CancellationToken ct = default) =>
        Http.GetMacroSeriesAsync(selection, ct);

    /// <summary>Estimates a macro export. Dispose the returned document.</summary>
    public Task<JsonDocument> MacroEstimateAsync(LseMacroSelection selection, string format = "parquet",
        CancellationToken ct = default) => Http.GetMacroEstimateAsync(selection, format, ct);

    /// <summary>Saves a macro export and returns its path.</summary>
    public Task<string> MacroExportAsync(LseMacroSelection selection, string format = "parquet",
        string? destinationDirectory = null, CancellationToken ct = default) =>
        SaveAsync(Http.ExportMacroAsync(selection, format, ct), destinationDirectory, ct);

    /// <summary>Saves a multi-series macro export and returns its path.</summary>
    public Task<string> MacroChartExportAsync(IReadOnlyList<string> codes, DateOnly start, DateOnly end,
        string mode = "level", string format = "csv", string? destinationDirectory = null, CancellationToken ct = default) =>
        SaveAsync(Http.ExportMacroChartAsync(codes, start, end, mode, format, ct), destinationDirectory, ct);

    /// <summary>Resolves an economic series code and reads its normalized date/value rows.</summary>
    public async Task<IReadOnlyList<LseSeriesPoint>> SeriesAsync(string symbol, DateOnly? start = null,
        DateOnly? end = null, string order = "asc", int limit = 5000, string? kind = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ValidateOrder(order, limit);
        var code = symbol.Trim();
        code = code.ToLowerInvariant() switch { "cpi_yoy" => "US.inflation_rate", "fdtr" => "US.interest_rate", _ => code };
        if (kind is null)
            (kind, code) = await ResolveSeriesAsync(code, ct).ConfigureAwait(false);
        using var doc = await MacroSeriesAsync(new(code, kind, start, end), ct).ConfigureAwait(false);
        var (dateField, valueField) = kind switch
        {
            "yield" => ("day", "close"), "release" => ("released_at", "actual"), _ => ("day", "value")
        };
        IEnumerable<JsonElement> rows = ArrayField(doc.RootElement, "rows").EnumerateArray();
        if (order == "desc")
            rows = rows.Reverse();
        return rows.Take(limit).Select(row => new LseSeriesPoint(code, Text(row, dateField),
            row.TryGetProperty(valueField, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : null)).ToArray();
    }

    /// <summary>Reads economic observations.</summary>
    public Task<IReadOnlyList<LseSeriesPoint>> EconomicsAsync(string symbol, DateOnly? start = null,
        DateOnly? end = null, int limit = 5000, CancellationToken ct = default) =>
        SeriesAsync(symbol, start, end, limit: limit, kind: "observation", ct: ct);

    /// <summary>Reads a bond-yield series.</summary>
    public Task<IReadOnlyList<LseSeriesPoint>> BondYieldsAsync(string symbol, DateOnly? start = null,
        DateOnly? end = null, int limit = 5000, CancellationToken ct = default) =>
        SeriesAsync(symbol, start, end, limit: limit, kind: "yield", ct: ct);

    /// <summary>Gets the reference catalogue or a series page. Dispose the returned document.</summary>
    public Task<JsonDocument> ReferenceCatalogueAsync(string? dataset = null, string? symbol = null,
        string? country = null, string? query = null, int? limit = null, int? offset = null,
        CancellationToken ct = default) => Http.GetReferenceCatalogueAsync(dataset, symbol, country, query, limit, offset, ct);

    /// <summary>Lists available reference collections.</summary>
    public Task<IReadOnlyList<JsonElement>> ReferenceAsync(CancellationToken ct = default) =>
        ListAsync(ReferenceCatalogueAsync(ct: ct), "datasets");

    /// <summary>Enumerates series in one reference collection.</summary>
    public IAsyncEnumerable<JsonElement> IterReferenceSeriesAsync(string dataset, string? country = null,
        string? query = null, int pageSize = 1000, CancellationToken ct = default)
    {
        ValidateCollectionPageSize(pageSize);
        return PagesAsync((offset, _, token) => ReferenceCatalogueAsync(dataset, country: country, query: query,
            limit: pageSize, offset: offset, ct: token), "series", pageSize, ct: ct);
    }

    /// <summary>Gets a reference preview. Dispose the returned document.</summary>
    public Task<JsonDocument> ReferencePreviewAsync(LseReferenceSelection selection, CancellationToken ct = default) =>
        Http.GetReferencePreviewAsync(selection, ct);

    /// <summary>Estimates a reference export. Dispose the returned document.</summary>
    public Task<JsonDocument> ReferenceEstimateAsync(LseReferenceSelection selection, string format = "parquet",
        CancellationToken ct = default) => Http.GetReferenceEstimateAsync(selection, format, ct);

    /// <summary>Saves a reference export and returns its path.</summary>
    public Task<string> ReferenceExportAsync(LseReferenceSelection selection, string format = "parquet",
        string? destinationDirectory = null, CancellationToken ct = default) =>
        SaveAsync(Http.ExportReferenceAsync(selection, format, ct), destinationDirectory, ct);

    /// <summary>Saves a named reference dataset, optionally filtered by symbol and dates.</summary>
    public Task<string> DatasetAsync(string name, string? symbol = null, DateOnly? start = null,
        DateOnly? end = null, string format = "parquet", string? destinationDirectory = null,
        CancellationToken ct = default) =>
        ReferenceExportAsync(new(name, symbol, From: start, To: end), format, destinationDirectory, ct);

    /// <summary>Reads the native CSV rows from a reference collection.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReferenceRowsAsync(
        LseReferenceSelection selection, CancellationToken ct = default) =>
        await ExportRowsAsync(Http.ExportReferenceAsync(selection, "csv", ct), ct).ConfigureAwait(false);

    /// <summary>Reads the economic calendar, optionally filtered by region and dates.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> EconomicCalendarAsync(string? region = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("economic_calendar", Country: region, From: start, To: end), ct);

    /// <summary>Reads company profiles.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> CompanyProfilesAsync(string? symbol = null,
        CancellationToken ct = default) => ReferenceRowsAsync(new("company_profiles", symbol), ct);

    /// <summary>Reads stock fundamentals.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> FundamentalsAsync(string? symbol = null,
        CancellationToken ct = default) => ReferenceRowsAsync(new("stock_fundamentals", symbol), ct);

    /// <summary>Reads insider transactions.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> InsiderTradesAsync(string? symbol = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("insider_trades", symbol, From: start, To: end), ct);

    /// <summary>Reads dividend history.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> DividendsAsync(string? symbol = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("dividends", symbol, From: start, To: end), ct);

    /// <summary>Reads stock splits.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> SplitsAsync(string? symbol = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("stock_splits", symbol, From: start, To: end), ct);

    /// <summary>Reads commitments of traders.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> CotAsync(string? symbol = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("cot", symbol, From: start, To: end), ct);

    /// <summary>Reads financial reports.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> FinancialReportsAsync(string? symbol = null,
        DateOnly? start = null, DateOnly? end = null, CancellationToken ct = default) =>
        ReferenceRowsAsync(new("financial_reports", symbol, From: start, To: end), ct);

    private async Task<(string Kind, string Code)> ResolveSeriesAsync(string code, CancellationToken ct)
    {
        foreach (var kind in new[] { "yield", "observation", "release" })
        {
            var matches = await ListAsync(MacroCatalogueAsync(new() { Kind = kind, Code = code, Limit = 1 }, ct),
                "series").ConfigureAwait(false);
            if (matches.Count > 0 && Text(matches[0], "code") is { } exact)
                return (kind, exact);
        }
        var found = await ListAsync(MacroCatalogueAsync(new() { Kind = "observation", Query = code, Limit = 20 }, ct),
            "series").ConfigureAwait(false);
        var names = found.Where(row => string.Equals(Text(row, "name"), code, StringComparison.OrdinalIgnoreCase)
            || Text(row, "code")?.EndsWith("." + code.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) == true)
            .Select(row => Text(row, "code")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = found.Select(row => Text(row, "code")).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name.Length).ToArray();
        if (names.Length == 1)
            return ("observation", names[0]);
        if (candidates.Length == 0)
            throw new LseException(404, "series-unknown", "No economic series matched the request.");
        if (names.Length == 0 && candidates.All(name => name == candidates[0]
            || name.StartsWith(candidates[0] + "_", StringComparison.OrdinalIgnoreCase)))
            return ("observation", candidates[0]);
        throw new LseException(0, "series-ambiguous", $"Multiple economic series matched: {string.Join(", ", candidates)}");
    }

    private static void ValidateCollectionPageSize(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 1000);
    }
}

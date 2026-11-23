using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LSE.NET;

/// <summary>Python-style entry point for LSE history, discovery, and live prices.</summary>
public sealed partial class LseClient : IDisposable
{
    private readonly string _key;
    private readonly Uri? _streamEndpoint;

    /// <summary>Uses the supplied key or LSE_API_KEY. An explicit stream endpoint disables automatic routing.</summary>
    public LseClient(string? apiKey = null, Uri? streamEndpoint = null)
        : this(new LseOptions { ApiKey = apiKey ?? Environment.GetEnvironmentVariable("LSE_API_KEY") ?? "" },
            streamEndpoint) { }

    /// <summary>Uses custom HTTP settings; owns the optional handler.</summary>
    public LseClient(LseOptions options, Uri? streamEndpoint = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Http = new(options, handler);
        _key = options.ApiKey;
        _streamEndpoint = streamEndpoint;
    }

    /// <summary>The underlying HTTP client, for vendor-specific routes and metadata.</summary>
    public LseDataClient Http { get; }

    /// <summary>Gets service status. Dispose the returned document.</summary>
    public Task<JsonDocument> HealthAsync(CancellationToken ct = default) => Http.GetHealthAsync(ct);

    /// <summary>Gets usage and entitlements. Dispose the returned document.</summary>
    public Task<JsonDocument> UsageAsync(CancellationToken ct = default) => Http.GetUsageAsync(ct);

    /// <summary>Lists markets as independent JSON objects.</summary>
    public Task<IReadOnlyList<JsonElement>> MarketsAsync(CancellationToken ct = default) =>
        ListAsync(Http.GetMarketsAsync(ct), "markets");

    /// <summary>Gets archive statistics. Dispose the returned document.</summary>
    public Task<JsonDocument> OverviewAsync(string? market = null, string? symbol = null, long? pairId = null,
        CancellationToken ct = default) => Http.GetOverviewAsync(market, symbol, pairId, ct);

    /// <summary>Gets the archive catalogue. Dispose the returned document.</summary>
    public Task<JsonDocument> CatalogueAsync(string? market = null, string? query = null, int? limit = null,
        int? offset = null, int? largest = null, CancellationToken ct = default) =>
        Http.GetCatalogueAsync(market, query, limit, offset, largest, ct);

    /// <summary>Gets one page of instruments. Dispose the returned document.</summary>
    public Task<JsonDocument> PairPageAsync(LsePairSearch search, CancellationToken ct = default) =>
        Http.SearchPairsAsync(search, ct);

    /// <summary>Yields instruments across catalogue pages, including cursor-paged options.</summary>
    public IAsyncEnumerable<JsonElement> IterPairsAsync(LsePairSearch search, int pageSize = 1000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 10000);
        return PagesAsync((offset, cursor, token) => PairPageAsync(
            search with { Offset = cursor is null ? offset : null, Cursor = cursor, Limit = pageSize }, token),
            search.GroupBy == "underlying" ? "underlyings" : "pairs", pageSize, search.Offset ?? 0, search.Cursor, ct);
    }

    /// <summary>Collects all matching instruments. Use IterPairsAsync for large catalogues.</summary>
    public async Task<IReadOnlyList<JsonElement>> PairsAsync(LsePairSearch search, CancellationToken ct = default)
    {
        var result = new List<JsonElement>();
        await foreach (var row in IterPairsAsync(search, ct: ct).ConfigureAwait(false))
            result.Add(row);
        return result;
    }

    /// <summary>Lists stored market tables.</summary>
    public Task<IReadOnlyList<JsonElement>> TablesAsync(string market, CancellationToken ct = default) =>
        ListAsync(Http.GetTablesAsync(market, ct), "tables");

    /// <summary>Gets split and dividend factors. Dispose the returned document.</summary>
    public Task<JsonDocument> FactorsAsync(string market, string? symbol = null, long? pairId = null,
        CancellationToken ct = default) => Http.GetFactorsAsync(market, symbol, pairId, ct);

    /// <summary>Previews a market selection. Dispose the returned document.</summary>
    public Task<JsonDocument> PreviewAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        Http.GetPreviewAsync(selection, ct);

    /// <summary>Estimates a market selection. Dispose the returned document.</summary>
    public Task<JsonDocument> EstimateAsync(LseMarketSelection selection, CancellationToken ct = default) =>
        Http.GetEstimateAsync(selection, ct);

    /// <summary>Saves a verified export to a directory and returns its absolute path.</summary>
    public Task<string> ExportAsync(LseMarketSelection selection, string? destinationDirectory = null,
        CancellationToken ct = default) => SaveAsync(Http.ExportAsync(selection, ct), destinationDirectory, ct);

    /// <summary>Saves a custom dataset export and returns its path.</summary>
    public Task<string> DatasetExportAsync(LseMarketSelection selection, LseDatasetFeatures features,
        string? destinationDirectory = null, CancellationToken ct = default) =>
        SaveAsync(Http.ExportDatasetAsync(selection, features, ct), destinationDirectory, ct);

    /// <summary>Downloads a market selection as native CSV rows. Empty CSV fields are null.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> RowsAsync(
        LseMarketSelection selection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return await ExportRowsAsync(Http.ExportAsync(selection with { Format = "csv" }, ct), ct).ConfigureAwait(false);
    }

    /// <summary>Gets a page of option contracts. Dispose the returned document.</summary>
    public Task<JsonDocument> OptionsChainAsync(LseOptionChainQuery query, CancellationToken ct = default) =>
        Http.GetOptionChainAsync(query, ct);

    /// <summary>Returns the contracts of one option-chain page.</summary>
    public Task<IReadOnlyList<JsonElement>> OptionsAsync(LseOptionChainQuery query, CancellationToken ct = default) =>
        ListAsync(OptionsChainAsync(query, ct), "rows");

    /// <summary>Gets a page of option prints with paging metadata. Dispose the returned document.</summary>
    public Task<JsonDocument> OptionsFlowPageAsync(LseOptionFlowQuery? query = null, CancellationToken ct = default) =>
        Http.GetOptionFlowAsync(query, ct);

    /// <summary>Returns the prints of one option-flow page.</summary>
    public Task<IReadOnlyList<JsonElement>> OptionsFlowAsync(LseOptionFlowQuery? query = null, CancellationToken ct = default) =>
        ListAsync(OptionsFlowPageAsync(query, ct), "rows");

    /// <summary>Lists option underlyings.</summary>
    public Task<IReadOnlyList<JsonElement>> OptionsUnderlyingsAsync(CancellationToken ct = default) =>
        PairsAsync(new() { Market = "us_options", GroupBy = "underlying" }, ct);

    private static async Task<IReadOnlyList<JsonElement>> ListAsync(Task<JsonDocument> request, string field)
    {
        using var doc = await request.ConfigureAwait(false);
        return ArrayField(doc.RootElement, field).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static JsonElement ArrayField(JsonElement root, string field)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(field, out var array)
            && array.ValueKind == JsonValueKind.Array)
            return array;
        throw new LseException(0, "invalid-response", $"The response did not contain a {field} array.");
    }

    private static async IAsyncEnumerable<JsonElement> PagesAsync(
        Func<int, string?, CancellationToken, Task<JsonDocument>> fetch, string field, int pageSize,
        int offset = 0, string? cursor = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (cursor is not null)
            seen.Add(cursor);
        while (true)
        {
            using var page = await fetch(offset, cursor, ct).ConfigureAwait(false);
            var root = page.RootElement;
            var rows = field == "underlyings" && root.ValueKind == JsonValueKind.Object && !root.TryGetProperty(field, out _)
                ? ArrayField(root, "pairs") : ArrayField(root, field);
            foreach (var row in rows.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                yield return row.Clone();
            }
            var count = rows.GetArrayLength();
            offset = checked(offset + count);
            var hasMore = root.TryGetProperty("has_more", out var more) ? more.ValueKind : JsonValueKind.Undefined;
            if (hasMore == JsonValueKind.False)
                yield break;
            if (root.TryGetProperty("next_cursor", out var next) || cursor is not null)
            {
                if (next.ValueKind == JsonValueKind.String && next.GetString() is { Length: > 0 } token
                    && count > 0 && seen.Add(token))
                {
                    cursor = token;
                    continue;
                }
                if (hasMore == JsonValueKind.True || next.ValueKind == JsonValueKind.String)
                    throw new LseException(0, "incomplete-catalogue", "The catalogue cursor did not advance.");
                yield break;
            }
            var hasTotal = root.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number
                && total.TryGetInt64(out _);
            if (hasMore != JsonValueKind.True && (hasTotal ? offset >= total.GetInt64() : count < pageSize))
                yield break;
            if (count == 0)
                throw new LseException(0, "incomplete-catalogue", "The catalogue returned an empty page before its end.");
        }
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ExportRowsAsync(
        Task<LseExport> download, CancellationToken ct)
    {
        try
        {
            return LseParsers.ParseRowsCsv((await download.ConfigureAwait(false)).Bytes, ct);
        }
        catch (LseException e) when (IsEmptySelection(e)) { return []; }
    }

    private static bool IsEmptySelection(LseException e) => e is { Status: 404, Code: "data-unavailable" }
        && e.Message.StartsWith("No stored data for this selection", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> SaveAsync(Task<LseExport> download, string? destinationDirectory, CancellationToken ct)
    {
        var file = await download.ConfigureAwait(false);
        var directory = Path.GetFullPath(destinationDirectory ?? ".");
        Directory.CreateDirectory(directory);
        var name = Path.GetFileName(file.FileName);
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Contains(':')
            || name.Any(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)))
            name = $"export.{file.Format}";
        var path = Path.Combine(directory, name);
        var temp = Path.Combine(directory, $".lse-{Guid.NewGuid():N}.part");
        var owned = false;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                owned = true;
                await stream.WriteAsync(file.Bytes, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: false);
            owned = false;
            return path;
        }
        finally { if (owned) File.Delete(temp); }
    }
}

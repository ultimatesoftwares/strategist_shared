using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LSE.NET;

/// <summary>
/// A client of LSE Data 3.0 (londonstrategicedge.com): one HTTPS service at
/// <see cref="LseOptions.BaseUrl"/>, every call a GET under /v3 with the key
/// in X-API-Key, answers JSON except the files (CSV or Parquet).
///
/// The client paces itself under the plan's calls-a-minute budget, retries a
/// call that got no answer or a 429/500/502/503/504, honours a short
/// Retry-After once per call, keeps one download open at a time (the
/// server's rule on every plan) and verifies each export's SHA-256 before
/// handing it over — a cut or corrupted transfer is requested again from the
/// start. A refusal throws <see cref="LseException"/>.
/// </summary>
public sealed partial class LseDataClient : IDisposable
{
    private readonly LseOptions _options;
    private readonly HttpClient _http;
    private readonly RateGate _rate;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _lifetime;
    private int _disposed;

    /// <summary>Creates a reusable client and snapshots its options.</summary>
    /// <param name="handler">Optional owned transport; it must not forward credentials on redirects.</param>
    public LseDataClient(LseOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        LseOptions.ValidateKey(options.ApiKey);
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint)
            || !LseOptions.IsSecureEndpoint(endpoint, "https", "http")
            || endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0)
            throw new ArgumentException("BaseUrl must be an HTTPS origin (HTTP loopback is allowed).", nameof(options));
        if (options.TimeoutSeconds is < 1 or > 3600 || options.Retries is < 0 or > 10
            || options.CallsPerMinute < 1 || options.MaxJsonBytes < 1 || options.MaxExportBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid HTTP timeout, retry, pacing or size limit.");
        ValidateFormat(options.TickFormat);
        _options = new LseOptions
        {
            ApiKey = options.ApiKey, BaseUrl = endpoint.AbsoluteUri,
            TimeoutSeconds = options.TimeoutSeconds, Retries = options.Retries,
            WaitForLimit = options.WaitForLimit, CallsPerMinute = options.CallsPerMinute,
            TickFormat = options.TickFormat.ToLowerInvariant(), Adjusted = options.Adjusted,
            MaxJsonBytes = options.MaxJsonBytes, MaxExportBytes = options.MaxExportBytes,
        };
        _rate = new RateGate(_options.CallsPerMinute, TimeSpan.FromMinutes(1));
        _lifetime = _shutdown.Token;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.BaseAddress = endpoint;
        _http.DefaultRequestHeaders.Add("X-API-Key", _options.ApiKey);
    }

    /// <summary>Is the data server up (/v3/health)? Dispose the result.</summary>
    public Task<JsonDocument> GetHealthAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v3/health", ct);

    /// <summary>What this key may do and has used (/v3/usage). Dispose the result.</summary>
    public Task<JsonDocument> GetUsageAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v3/usage", ct);

    /// <summary>Lists markets visible to the key. Dispose the result.</summary>
    public Task<JsonDocument> GetMarketsAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v3/markets", ct);

    /// <summary>Searches one page of the instrument catalogue. Dispose the result.</summary>
    public Task<JsonDocument> SearchPairsAsync(string? market = null, string? query = null,
        int limit = 25, int offset = 0, CancellationToken ct = default)
    {
        return SearchPairsAsync(new LsePairSearch { Market = market, Query = query, Limit = limit, Offset = offset }, ct);
    }

    /// <summary>
    /// Finds one instrument by its exact catalogue symbol, in any case
    /// (/v3/pairs?symbol=). Without <paramref name="market"/> the server
    /// searches every enabled market in a fixed order, so "AAPL" resolves to
    /// US stocks. Null when the symbol is not listed.
    /// </summary>
    public async Task<LsePair?> FindPairAsync(
        string symbol, string? market = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var path = "/v3/pairs?" + Query(
            ("market", market), ("symbol", symbol), ("limit", "1"));
        using var doc = await GetJsonAsync(path, ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("pairs", out var pairs) || pairs.ValueKind != JsonValueKind.Array)
            throw new LseException(0, "invalid-response", "The catalogue did not return a pairs array.");
        if (pairs.GetArrayLength() == 0)
            return null;
        var pair = pairs[0];
        if (pair.ValueKind != JsonValueKind.Object
            || ReadString(pair, "folder") is not { Length: > 0 } folder
            || ReadString(pair, "symbol") is not { Length: > 0 } listedSymbol
            || !pair.TryGetProperty("pair_id", out var id) || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt64(out var pairId))
            throw new LseException(0, "invalid-response", "The catalogue did not return a valid instrument.");
        return new LsePair(folder, listedSymbol, pairId, ReadString(pair, "name") ?? "");
    }

    /// <summary>
    /// OHLCV candles of [range.From, range.To) as rows, oldest first, via a
    /// CSV export held in memory (/v3/export, data=candles). A selection
    /// with no stored rows (a weekend, a gap) is an empty list, not an error.
    /// </summary>
    public async Task<IReadOnlyList<LseCandle>> GetCandlesAsync(
        string market, string symbol, string timeframe, LseDateRange range,
        CancellationToken ct = default)
    {
        var export = await TryExportAsync(market, symbol, timeframe, range, "csv", "candles", ct).ConfigureAwait(false);
        return export is null ? [] : LseParsers.ParseCandlesCsv(export.Bytes, ct);
    }

    /// <summary>
    /// The trade slice of [range.From, range.To) as rows, oldest first
    /// (/v3/export, timeframe=tick), in <see cref="LseOptions.TickFormat"/>.
    /// Empty when nothing is stored for the window. Quote-priced markets
    /// (forex, commodities) have no trade slice and throw 404 data-unavailable
    /// with the server's explanation.
    /// </summary>
    public async Task<IReadOnlyList<LseTrade>> GetTradesAsync(
        string market, string symbol, LseDateRange range, CancellationToken ct = default)
    {
        var parquet = _options.TickFormat == "parquet";
        var export = await TryExportAsync(
            market, symbol, "tick", range, parquet ? "parquet" : "csv", "candles", ct).ConfigureAwait(false);
        if (export is null)
            return [];
        return parquet
            ? await LseParsers.ParseTradesParquetAsync(export.Bytes, ct).ConfigureAwait(false)
            : LseParsers.ParseTradesCsv(export.Bytes, ct);
    }

    /// <summary>Estimates an export (/v3/estimate). Dispose the result.</summary>
    public Task<JsonDocument> GetEstimateAsync(
        string market, string symbol, string timeframe, LseDateRange range,
        string format = "csv", string data = "candles", CancellationToken ct = default) =>
        GetEstimateAsync(HistorySelection(market, symbol, timeframe, range, format, data), ct);

    /// <summary>
    /// Downloads one export whole (/v3/export): the server sends the file at
    /// once with its SHA-256 in x-checksum-sha256, which is verified here; a
    /// transfer that is cut or fails the checksum is requested again from the
    /// start, up to the retry budget. One download runs at a time.
    /// </summary>
    public Task<LseExport> ExportAsync(
        string market, string symbol, string timeframe, LseDateRange range,
        string format = "csv", string data = "candles", CancellationToken ct = default) =>
        ExportMarketAsync("/v3/export", HistorySelection(market, symbol, timeframe, range, format, data),
            ct, $"{symbol}_{market}_{timeframe}.{format}");

    private async Task<LseExport> DownloadAsync(string path, string format, string fallbackName, CancellationToken ct)
    {
        using var lifetime = Link(ct);
        await _downloadGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            return await ExecuteAsync(path, async (response, token) =>
            {
                var name = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? fallbackName;
                var fileName = Path.GetFileName(name.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
                    fileName = $"export.{format.ToLowerInvariant()}";
                var expected = Header(response, "x-checksum-sha256");
                var rows = long.TryParse(Header(response, "x-export-rows"), CultureInfo.InvariantCulture, out var r) && r >= 0 ? r : -1;
                var bytes = await ReadBodyAsync(response.Content, _options.MaxExportBytes, token).ConfigureAwait(false);
                if (expected is not null)
                {
                    var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new LseException(0, "checksum-mismatch",
                            "The export did not match the server's SHA-256 checksum.");
                }
                return new LseExport(bytes, fileName, rows, format, expected is not null)
                {
                    ExportId = Header(response, "x-export-id"),
                    Adjusted = Header(response, "x-lse3-adjusted") switch { "1" => true, "0" => false, _ => null }
                };
            }, lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>An export, with "no stored rows" (404 data-unavailable with
    /// the no-data message) answered as null instead of thrown.</summary>
    private async Task<LseExport?> TryExportAsync(
        string market, string symbol, string timeframe, LseDateRange range,
        string format, string data, CancellationToken ct)
    {
        try
        {
            return await ExportAsync(market, symbol, timeframe, range, format, data, ct).ConfigureAwait(false);
        }
        catch (LseException e) when (
            e is { Status: 404, Code: "data-unavailable" }
            && e.Message.StartsWith("No stored data for this selection", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    /// <summary>GETs a JSON endpoint under /v3 on the configured origin. Dispose the result.</summary>
    public Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken ct = default) =>
        ReadJsonAsync(pathAndQuery, ct);

    internal async Task<JsonDocument> ReadJsonAsync(string pathAndQuery, CancellationToken ct, string prefix = "/v3/")
    {
        using var lifetime = Link(ct);
        return await ExecuteAsync(pathAndQuery, async (response, token) =>
        {
            var bytes = await ReadBodyAsync(response.Content, _options.MaxJsonBytes, token).ConfigureAwait(false);
            try { return JsonDocument.Parse(bytes.AsSpan().StartsWith("\uFEFF"u8) ? bytes.AsMemory(3) : bytes); }
            catch (JsonException)
            {
                throw new LseException((int)response.StatusCode, "invalid-response", "Expected a JSON response.");
            }
        }, lifetime.Token, prefix).ConfigureAwait(false);
    }

    private async Task<T> ExecuteAsync<T>(string pathAndQuery,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken ct, string prefix = "/v3/")
    {
        var path = pathAndQuery?.Split('?', 2)[0];
        if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal)
            || path.Contains('%') || path.Contains('\\') || pathAndQuery!.Contains('#')
            || !Uri.TryCreate(_http.BaseAddress, pathAndQuery, out var uri)
            || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            || uri.GetLeftPart(UriPartial.Authority) != _http.BaseAddress!.GetLeftPart(UriPartial.Authority))
            throw new ArgumentException("Use an origin-relative /v3/ path and encode parameters in the query.", nameof(pathAndQuery));

        var retriesLeft = _options.Retries;
        var waitedForLimit = false;
        while (true)
        {
            await _rate.WaitAsync(ct).ConfigureAwait(false);
            LseException error;
            try
            {
                HttpResponseMessage headers;
                using (var timeout = ReadTimeout(ct))
                    headers = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                using var response = headers;
                if (response.IsSuccessStatusCode)
                    return await read(response, ct).ConfigureAwait(false);
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                var body = await ReadBodyAsync(response.Content, 64 * 1024, ct, truncate: true).ConfigureAwait(false);
                error = LseException.Parse((int)response.StatusCode, Encoding.UTF8.GetString(body), retryAfter, _options.ApiKey);
            }
            catch (LseException e) when (e.Code == "checksum-mismatch") { error = e; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { error = new LseException(0, "timeout", "The HTTP response timed out."); }
            catch (Exception e) when (e is HttpRequestException or IOException)
            { error = new LseException(0, "no-answer", "The HTTP response could not be completed."); }

            if (error.Status is 429 or 503 && error.RetryAfter is { } delay && delay > TimeSpan.Zero)
            {
                if (!_options.WaitForLimit || waitedForLimit || delay > TimeSpan.FromMinutes(2))
                    throw error;
                waitedForLimit = true;
                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }
            if (error.Status is not (0 or 429 or 500 or 502 or 503 or 504) || retriesLeft-- <= 0)
                throw error;
            var seconds = Math.Min(8, 1 << Math.Min(3, _options.Retries - retriesLeft - 1));
            await Task.Delay(TimeSpan.FromMilliseconds(seconds * 1000 + Random.Shared.Next(250)), ct).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpContent content, int maximum, CancellationToken ct, bool truncate = false)
    {
        var length = content.Headers.ContentLength;
        if (!truncate && length > maximum)
            throw new LseException(0, "response-too-large", $"The response exceeds {maximum} bytes.");

        using var timeout = ReadTimeout(ct);
        var stream = await content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var body = new MemoryStream((int)Math.Min(length ?? 0, Math.Min(maximum, 1024 * 1024)));
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            while (true)
            {
                var remaining = maximum - body.Length;
                if (truncate && remaining == 0)
                    break;
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), timeout.Token)
                    .ConfigureAwait(false);
                if (count == 0)
                    break;
                if (count > remaining && !truncate)
                    throw new LseException(0, "response-too-large", $"The response exceeds {maximum} bytes.");
                body.Write(buffer, 0, (int)Math.Min(count, remaining));
            }
            if (!truncate && length is { } declared && body.Length != declared)
                throw new IOException("Incomplete HTTP response.");
            return body.ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private CancellationTokenSource Link(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime);
    }

    private CancellationTokenSource ReadTimeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        return timeout;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static LseMarketSelection HistorySelection(
        string market, string symbol, string timeframe, LseDateRange range,
        string format, string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);
        ArgumentException.ThrowIfNullOrWhiteSpace(data);
        return new(market) { Symbol = symbol, Timeframe = timeframe, Window = range, Format = format, Data = data };
    }

    private static void ValidateFormat(string format)
    {
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(format, "parquet", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected csv or parquet.", nameof(format));
    }

    private static string Query(params (string Key, string? Value)[] fields) =>
        string.Join('&', fields
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value!)}"));

    private static string FormatTime(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    /// <summary>Cancels active requests and releases the owned transport.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _shutdown.Cancel();
        _http.Dispose();
        _shutdown.Dispose();
    }
}

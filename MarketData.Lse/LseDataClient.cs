using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Strategist.TimeSeries;

namespace MarketData.Lse;

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
public sealed class LseDataClient : IDisposable
{
    private readonly LseOptions _options;
    private readonly HttpClient _http;
    private readonly RateGate _rate;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    /// <param name="handler">Overrides the transport (tests).</param>
    public LseDataClient(LseOptions options, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("the LSE API key is required", nameof(options));
        _options = options;
        _rate = new RateGate(Math.Max(1, options.CallsPerMinute), TimeSpan.FromMinutes(1));
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // Per-call timeouts cover the header phase only; a large export may
        // legitimately stream for longer than any fixed budget.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        _http.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
    }

    // ---- The calls the history source needs ----

    /// <summary>Is the data server up (/v3/health).</summary>
    public Task<JsonDocument> GetHealthAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v3/health", ct);

    /// <summary>What this key may do and has used (/v3/usage).</summary>
    public Task<JsonDocument> GetUsageAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v3/usage", ct);

    /// <summary>
    /// Finds one instrument by its exact catalogue symbol, in any case
    /// (/v3/pairs?symbol=). Without <paramref name="market"/> the server
    /// searches every enabled market in a fixed order, so "AAPL" resolves to
    /// US stocks. Null when the symbol is not listed.
    /// </summary>
    public async Task<LsePair?> FindPairAsync(
        string symbol, string? market = null, CancellationToken ct = default)
    {
        var path = "/v3/pairs?" + Query(
            ("market", market), ("symbol", symbol), ("limit", "1"));
        using var doc = await GetJsonAsync(path, ct);
        if (!doc.RootElement.TryGetProperty("pairs", out var pairs)
            || pairs.ValueKind != JsonValueKind.Array
            || pairs.GetArrayLength() == 0)
            return null;
        var pair = pairs[0];
        return new LsePair(
            pair.GetProperty("folder").GetString() ?? "",
            pair.GetProperty("symbol").GetString() ?? "",
            pair.TryGetProperty("pair_id", out var id) ? id.GetInt64() : 0,
            pair.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "");
    }

    /// <summary>
    /// OHLCV candles of [range.From, range.To) as rows, oldest first, via a
    /// CSV export held in memory (/v3/export, data=candles). A selection
    /// with no stored rows (a weekend, a gap) is an empty list, not an error.
    /// </summary>
    public async Task<IReadOnlyList<LseCandle>> GetCandlesAsync(
        string market, string symbol, string timeframe, DateRange range,
        CancellationToken ct = default)
    {
        var export = await TryExportAsync(market, symbol, timeframe, range, "csv", "candles", ct);
        return export is null ? [] : LseParsers.ParseCandlesCsv(export.Bytes);
    }

    /// <summary>
    /// The trade slice of [range.From, range.To) as rows, oldest first
    /// (/v3/export, timeframe=tick), in <see cref="LseOptions.TickFormat"/>.
    /// Empty when nothing is stored for the window. Quote-priced markets
    /// (forex, commodities) have no trade slice and throw 404 data-unavailable
    /// with the server's explanation.
    /// </summary>
    public async Task<IReadOnlyList<LseTrade>> GetTradesAsync(
        string market, string symbol, DateRange range, CancellationToken ct = default)
    {
        var parquet = !string.Equals(_options.TickFormat, "csv", StringComparison.OrdinalIgnoreCase);
        var export = await TryExportAsync(
            market, symbol, "tick", range, parquet ? "parquet" : "csv", "candles", ct);
        if (export is null)
            return [];
        return parquet
            ? await LseParsers.ParseTradesParquetAsync(export.Bytes, ct)
            : LseParsers.ParseTradesCsv(export.Bytes);
    }

    /// <summary>What an export would weigh before asking for it (/v3/estimate).</summary>
    public Task<JsonDocument> GetEstimateAsync(
        string market, string symbol, string timeframe, DateRange range,
        string format = "csv", string data = "candles", CancellationToken ct = default) =>
        GetJsonAsync("/v3/estimate?" + SelectionQuery(market, symbol, timeframe, range, format, data), ct);

    /// <summary>
    /// Downloads one export whole (/v3/export): the server sends the file at
    /// once with its SHA-256 in x-checksum-sha256, which is verified here; a
    /// transfer that is cut or fails the checksum is requested again from the
    /// start, up to the retry budget. One download runs at a time.
    /// </summary>
    public async Task<LseExport> ExportAsync(
        string market, string symbol, string timeframe, DateRange range,
        string format = "csv", string data = "candles", CancellationToken ct = default)
    {
        var path = "/v3/export?" + SelectionQuery(market, symbol, timeframe, range, format, data);
        await _downloadGate.WaitAsync(ct);
        try
        {
            var attempts = Math.Max(1, _options.Retries + 1);
            for (var attempt = 1; ; attempt++)
            {
                using var response = await SendAsync(path, ct);
                var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? $"{symbol}_{market}_{timeframe}.{format}";
                var expected = Header(response, "x-checksum-sha256");
                var rows = long.TryParse(Header(response, "x-export-rows"), out var r) ? r : -1;
                byte[] bytes;
                try
                {
                    bytes = await response.Content.ReadAsByteArrayAsync(ct);
                }
                catch (Exception e) when (
                    e is HttpRequestException or IOException && attempt < attempts)
                {
                    continue;  // The transfer was cut: ask again from the start.
                }
                if (expected is not null)
                {
                    var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        if (attempt < attempts)
                            continue;
                        throw new LseException(0, "checksum-mismatch",
                            $"export {fileName}: SHA-256 {actual} does not match the server's {expected}");
                    }
                }
                return new LseExport(bytes, fileName, rows, format, expected is not null);
            }
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>An export, with "no stored rows" (404 data-unavailable with
    /// the no-data message) answered as null instead of thrown.</summary>
    private async Task<LseExport?> TryExportAsync(
        string market, string symbol, string timeframe, DateRange range,
        string format, string data, CancellationToken ct)
    {
        try
        {
            return await ExportAsync(market, symbol, timeframe, range, format, data, ct);
        }
        catch (LseException e) when (
            e is { Status: 404, Code: "data-unavailable" }
            && e.Message.StartsWith("No stored data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    // ---- Transport ----

    /// <summary>GETs a JSON answer, with the shared retry/pacing rules.</summary>
    public async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken ct = default)
    {
        using var response = await SendAsync(pathAndQuery, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>
    /// One paced GET with the client's retry rules: a call that got no answer
    /// or a 429/500/502/503/504 is retried up to <see cref="LseOptions.Retries"/>
    /// times; a 429 with a Retry-After of two minutes or less is waited out
    /// once per call without using up a retry. A refusal that stands throws.
    /// The caller disposes the successful response.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string pathAndQuery, CancellationToken ct)
    {
        var retriesLeft = Math.Max(0, _options.Retries);
        var waitedForLimit = false;
        while (true)
        {
            await _rate.WaitAsync(ct);
            HttpResponseMessage response;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
                try
                {
                    response = await _http.GetAsync(
                        pathAndQuery, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                }
                catch (Exception e) when (
                    e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    if (retriesLeft-- > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                        continue;
                    }
                    throw new LseException(0, "no-answer",
                        $"no answer from {_options.BaseUrl}: {e.Message}");
                }
            }
            if (response.IsSuccessStatusCode)
                return response;

            var body = await response.Content.ReadAsStringAsync(ct);
            var retryAfterHeader = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            var status = (int)response.StatusCode;
            response.Dispose();
            var error = LseException.Parse(status, body, retryAfterHeader);

            if (status == 429 && _options.WaitForLimit && !waitedForLimit
                && error.RetryAfter is { } retryAfter
                && retryAfter > TimeSpan.Zero && retryAfter <= TimeSpan.FromMinutes(2))
            {
                waitedForLimit = true;
                await Task.Delay(retryAfter, ct);
                continue;
            }
            if (status is 429 or 500 or 502 or 503 or 504 && retriesLeft-- > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }
            throw error;
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private string SelectionQuery(
        string market, string symbol, string timeframe, DateRange range,
        string format, string data) =>
        Query(
            ("market", market), ("symbol", symbol), ("timeframe", timeframe),
            ("from", FormatTime(range.From)), ("to", FormatTime(range.To)),
            ("format", format), ("data", data),
            ("adjusted", _options.Adjusted ? "1" : "0"));

    private static string Query(params (string Key, string? Value)[] fields) =>
        string.Join('&', fields
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value!)}"));

    /// <summary>The server's UTC timestamp spelling: "yyyy-MM-dd HH:mm:ss",
    /// upper bound excluded — the same half-open window as <see cref="DateRange"/>.</summary>
    internal static string FormatTime(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _http.Dispose();
        _downloadGate.Dispose();
    }
}

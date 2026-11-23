using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LSE.NET;

namespace Lse.Tests;

public sealed class HttpTests
{
    private const string Key = "test-key-not-live";
    private static readonly LseDateRange Window = new(
        new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.FromHours(3)).AddTicks(123),
        new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.FromHours(3)));

    [Fact]
    public async Task HistoryOverloadsMatchSelectionRequestsAndKeepTheirSuggestedFilename()
    {
        var sent = new List<Uri>();
        using var client = Client((request, _) =>
        {
            sent.Add(request.RequestUri!);
            return Reply(HttpStatusCode.OK, "{}");
        });
        var selection = new LseMarketSelection("us_stocks")
            { Symbol = "AAPL", Timeframe = "1d", Window = Window, Format = "CSV", Data = "candles" };

        using var historyEstimate = await client.GetEstimateAsync("us_stocks", "AAPL", "1d", Window, "CSV");
        using var selectionEstimate = await client.GetEstimateAsync(selection);
        var historyExport = await client.ExportAsync("us_stocks", "AAPL", "1d", Window, "CSV");
        var selectionExport = await client.ExportAsync(selection);

        Assert.Equal(sent[0], sent[1]);
        Assert.Equal(sent[2], sent[3]);
        Assert.Equal("AAPL_us_stocks_1d.CSV", historyExport.FileName);
        Assert.Equal("export.csv", selectionExport.FileName);
    }

    [Fact]
    public async Task ExportsEncodeSelectionAndVerifyChecksum()
    {
        var bytes = "time,price,size,kind\n2026-10-23 09:00:00.123456789,12.25,4,\"trade, odd lot\"\n"u8.ToArray();
        using var client = Client((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("X-API-Key")));
            var uri = request.RequestUri!;
            Assert.Equal("/v3/export", uri.AbsolutePath);
            Assert.Contains("symbol=EUR%2FUSD%26x", uri.Query);
            Assert.Contains("from=2026-10-23%2009%3A00%3A00.0000123", uri.Query);
            var response = new HttpResponseMessage { Content = new ByteArrayContent(bytes) };
            response.Headers.Add("x-export-rows", "1");
            response.Headers.Add("x-checksum-sha256", Convert.ToHexString(SHA256.HashData(bytes)));
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            { FileNameStar = "../../data.csv" };
            return Task.FromResult(response);
        });
        var export = await client.ExportAsync("us_stocks", "EUR/USD&x", "tick", Window);
        Assert.True(export.ChecksumVerified);
        Assert.Equal(1, export.Rows);
        Assert.Equal("data.csv", export.FileName);
        var trade = Assert.Single(await client.GetTradesAsync("us_stocks", "EUR/USD&x", Window));
        Assert.Equal(new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc).AddTicks(1234567), trade.Time);
        Assert.Equal("trade, odd lot", trade.Kind);
    }

    [Theory]
    [InlineData("https://example.net/v3/health")]
    [InlineData("//example.net/v3/health")]
    [InlineData("/v3/../elsewhere")]
    [InlineData("/v3/%2E%2E/elsewhere")]
    [InlineData("/v3/health#fragment")]
    public async Task RejectsUnsafeRequestTargets(string target)
    {
        using var client = Client((_, _) => throw new InvalidOperationException("Request was sent."));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetJsonAsync(target));
    }

    [Fact]
    public async Task HeadersAndChecksumFailuresShareOneRetryBudget()
    {
        var attempts = 0;
        using var client = Client((_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                return Reply(HttpStatusCode.ServiceUnavailable, "{}");
            var reply = new HttpResponseMessage { Content = new StringContent("bad data") };
            reply.Headers.Add("x-checksum-sha256", new string('0', 64));
            return Task.FromResult(reply);
        }, retries: 1);
        var error = await Assert.ThrowsAsync<LseException>(() => client.ExportAsync("us_stocks", "AAPL", "1d", Window));
        Assert.Equal("checksum-mismatch", error.Code);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryAfterIsPreservedWithoutEarlyRetryOrKeyDisclosure()
    {
        var attempts = 0;
        using var client = Client((_, _) =>
        {
            attempts++;
            var result = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent($$"""{"code":"calls-exhausted","message":"key {{Key}}","retry_after":1e300}""")
            };
            result.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
            return Task.FromResult(result);
        }, retries: 3);
        var error = await Assert.ThrowsAsync<LseException>(() => client.GetUsageAsync());
        Assert.Equal(TimeSpan.FromMinutes(3), error.RetryAfter);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task EmptySelectionIsNotConfusedWithUnavailableTrades()
    {
        using var empty = Client((_, _) => Reply(HttpStatusCode.NotFound,
            """{"code":"data-unavailable","message":"No stored data for this selection"}"""));
        Assert.Empty(await empty.GetCandlesAsync("us_stocks", "AAPL", "1d", Window));
        using var unavailable = Client((_, _) => Reply(HttpStatusCode.NotFound,
            """{"code":"data-unavailable","message":"This market stores quotes, not trades."}"""));
        var error = await Assert.ThrowsAsync<LseException>(() => unavailable.GetTradesAsync("forex", "EUR_USD", Window));
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task RejectsOversizeResponsesAndPreservesCancellation()
    {
        using var client = Client((_, _) => Reply(HttpStatusCode.OK, """{"long":"value"}"""), maxJsonBytes: 4);
        Assert.Equal("response-too-large", (await Assert.ThrowsAsync<LseException>(() => client.GetHealthAsync())).Code);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetHealthAsync(cancel.Token));
    }

    [Fact]
    public async Task DisposalCancelsARequestWaitingForTheRateGate()
    {
        var sends = 0;
        using var client = new LseDataClient(new() { ApiKey = Key, CallsPerMinute = 1 },
            new Handler((_, _) =>
            {
                Interlocked.Increment(ref sends);
                return Reply(HttpStatusCode.OK, "{}");
            }));
        using var first = await client.GetHealthAsync();
        var second = client.GetHealthAsync();
        Assert.False(second.IsCompleted);
        client.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task CatalogueOptionsAreSnapshotted()
    {
        var options = new LseOptions { ApiKey = Key, Retries = 0 };
        using var client = new LseDataClient(options, new Handler((req, _) =>
        {
            Assert.Equal(Key, Assert.Single(req.Headers.GetValues("X-API-Key")));
            Assert.Contains("q=apple%20%26%20pear", req.RequestUri!.Query);
            return Reply(HttpStatusCode.OK, """{"pairs":[]}""");
        }));
        options.ApiKey = "mutated";
        using var reply = await client.SearchPairsAsync("us_stocks", "apple & pear");
        Assert.Equal(0, reply.RootElement.GetProperty("pairs").GetArrayLength());
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static LseDataClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        int retries = 0, int maxJsonBytes = 8192) => new(
        new LseOptions { ApiKey = Key, TickFormat = "csv", Retries = retries, MaxJsonBytes = maxJsonBytes }, new Handler(send));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}

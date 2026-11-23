using System.Net.WebSockets;
using LSE.NET;

namespace Lse.Tests;

public sealed class LseClientTests
{
    [Fact]
    public async Task CandlesResolveSymbolAndUseInclusiveDates()
    {
        using var client = Client(uri => uri.AbsolutePath switch
        {
            "/v3/pairs" => """{"pairs":[{"folder":"us_stocks","symbol":"AAPL","pair_id":42}]}""",
            "/v3/export" when uri.Query.Contains("from=2026-11-06&to=2026-11-06") =>
                "time,open,high,low,close,volume,trades\n2026-11-06 15:00:00,1,2,1,2,100,10\n",
            _ => throw new InvalidOperationException(uri.AbsoluteUri)
        });
        var date = new DateOnly(2026, 11, 6);
        var bars = await client.CandlesAsync("aapl", start: date, end: date);
        Assert.Equal(2, Assert.Single(bars).Close);
    }

    [Fact]
    public async Task IterPairsFollowsAnOpaqueCursor()
    {
        using var client = Client(uri => uri.Query.Contains("cursor=a%2Bb")
            ? """{"pairs":[{"symbol":"B"}],"has_more":false}"""
            : """{"pairs":[{"symbol":"A"}],"has_more":true,"next_cursor":"a+b"}""");
        var actual = new List<string?>();
        await foreach (var row in client.IterPairsAsync(new() { Market = "us_options" }, 1))
            actual.Add(row.GetProperty("symbol").GetString());
        Assert.Equal(["A", "B"], actual);
    }

    [Fact]
    public async Task SeriesResolvesYieldAndNormalizesItsRows()
    {
        using var client = Client(uri => uri.AbsolutePath switch
        {
            "/v3/macro" when uri.Query.Contains("kind=yield") => """{"series":[{"code":"US10Y"}]}""",
            "/v3/macro/series" when uri.Query.Contains("kind=yield") => """{"rows":[{"day":"2026-11-06","close":4.5}]}""",
            _ => throw new InvalidOperationException(uri.AbsoluteUri)
        });
        var point = Assert.Single(await client.SeriesAsync("US10Y"));
        Assert.Equal("2026-11-06", point.Date);
        Assert.Equal(4.5, point.Value!.Value.GetDouble());
    }

    [Fact]
    public async Task DatasetSavesTheReferenceExportWithoutOverwriting()
    {
        const string csv = "symbol,name\nAAPL,Apple\n";
        using var client = Client(uri => uri.AbsolutePath == "/v3/reference/export"
            && uri.Query.Contains("dataset=company_profiles&symbol=AAPL")
            && uri.Query.Contains("format=csv") ? csv : throw new InvalidOperationException(uri.AbsoluteUri));
        var directory = Directory.CreateTempSubdirectory("lse-facade-");
        try
        {
            var path = await client.DatasetAsync("company_profiles", "AAPL", format: "csv",
                destinationDirectory: directory.FullName);
            Assert.Equal(csv, await File.ReadAllTextAsync(path));
            await Assert.ThrowsAsync<IOException>(() => client.DatasetAsync("company_profiles", "AAPL",
                format: "csv", destinationDirectory: directory.FullName));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(directory.FullName)));
        }
        finally
        {
            foreach (var file in directory.EnumerateFiles())
                file.Delete();
            directory.Delete();
        }
    }

    [Fact]
    public async Task RoutesMixedSymbolsAndSupportsSubscriptionChanges()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var unsubscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var main = await StreamTests.StartAsync(async (ws, ct) =>
        {
            await Authenticate(ws, """{"type":"authenticated","symbols":[{"symbol":"BTC/USD","category":"Crypto"}]}""", ct);
            Assert.Equal("AAPL", (await StreamTests.Receive(ws, ct)).GetProperty("symbol").GetString());
            await StreamTests.Send(ws, """{"type":"tick","symbol":"AAPL","price":150,"ts":"2026-11-06T15:00:00Z"}""", ct);
            var unsubscribe = await StreamTests.Receive(ws, ct);
            Assert.Equal("unsubscribe", unsubscribe.GetProperty("action").GetString());
            Assert.Equal("AAPL", unsubscribe.GetProperty("symbol").GetString());
            unsubscribed.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, deadline.Token);
        await using var london = await StreamTests.StartAsync(async (ws, ct) =>
        {
            await Authenticate(ws, """{"type":"authenticated"}""", ct);
            Assert.Equal("BTC/USD", (await StreamTests.Receive(ws, ct)).GetProperty("symbol").GetString());
            await StreamTests.Send(ws, """{"type":"tick","symbol":"BTC/USD","price":100,"ts":"2026-11-06T15:00:00Z"}""", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, deadline.Token);
        using var client = new LseClient("fixture-key")
        {
            DataStreamEndpoint = new UriBuilder(main.Urls.Single()) { Scheme = "ws" }.Uri,
            LondonStreamEndpoint = new UriBuilder(london.Urls.Single()) { Scheme = "ws" }.Uri
        };
        var symbols = new HashSet<string>();
        await foreach (var tick in client.StreamAsync(["BTC/USD", "AAPL"], deadline.Token))
        {
            symbols.Add(tick.Symbol);
            if (symbols.Count != 2)
                continue;
            await client.UnsubscribeAsync(["AAPL"], deadline.Token);
            await unsubscribed.Task.WaitAsync(deadline.Token);
            client.Disconnect();
        }
        Assert.Equal(2, symbols.Count);
        deadline.Cancel();
    }

    private static async Task Authenticate(WebSocket ws, string response, CancellationToken ct)
    {
        await StreamTests.Send(ws, """{"type":"welcome"}""", ct);
        Assert.Equal("auth", (await StreamTests.Receive(ws, ct)).GetProperty("action").GetString());
        await StreamTests.Send(ws, response, ct);
    }

    private static LseClient Client(Func<Uri, string> respond) =>
        new(new LseOptions { ApiKey = "fixture-key", Retries = 0 }, handler: new Handler(respond));

    private sealed class Handler(Func<Uri, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage { Content = new StringContent(respond(request.RequestUri!)) });
    }
}

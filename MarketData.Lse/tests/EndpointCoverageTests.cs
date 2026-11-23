using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LSE.NET;

namespace Lse.Tests;

public sealed class EndpointCoverageTests
{
    [Fact]
    public async Task MapsArchiveOptionsMacroAndReferenceJsonRoutes()
    {
        using var handler = new Capture();
        using var client = new LseDataClient(new() { ApiKey = "fixture-key", Retries = 0 }, handler);
        var date = new DateOnly(2026, 11, 6);
        var market = new LseMarketSelection("cme") { PairId = 4000658762, Data = "book", Timeframe = "tick" };
        var macro = new LseMacroSelection("US.core_inflation_rate", From: date, To: date);
        var reference = new LseReferenceSelection("earnings_calendar", "BRK.B", "US", "Berkshire & Co", date, date);
        (Func<Task<System.Text.Json.JsonDocument>> Run, string Path, string Query)[] cases =
        [
            (() => client.SearchPairsAsync(new LsePairSearch { Market = "cme", Cursor = "a+/=", GroupBy = "underlying",
                Underlying = "SP500", Product = "ES", Expiry = date, InstrumentType = "option", OptionType = "put", Limit = 0 }),
                "/v3/pairs", "cursor=a%2B%2F%3D"),
            (() => client.GetCatalogueAsync(largest: 5), "/v3/catalogue", "largest=5"),
            (() => client.GetFactorsAsync("us_stocks", pairId: 2000000040), "/v3/factors", "pair_id=2000000040"),
            (() => client.GetTablesAsync("cme"), "/v3/tables", "market=cme"),
            (() => client.GetOverviewAsync("us_stocks", "BRK.B"), "/v3/overview", "symbol=BRK.B"),
            (() => client.GetPreviewAsync(market), "/v3/preview", "data=book"),
            (() => client.GetEstimateAsync(market), "/v3/estimate", "pair_id=4000658762"),
            (() => client.GetOptionChainAsync(new("AAPL") { StrikeMin = 1.25m, MinDaysToExpiry = 0 }),
                "/v3/options/chain", "strike_min=1.25"),
            (() => client.GetOptionFlowAsync(new() { Underlying = "AAPL", Type = "put", Expiry = date, MinPremium = 100000 }),
                "/v3/options/flow", "type=put"),
            (() => client.SearchMacroAsync(new() { Kind = "yield", Group = "credit", Country = "US", Limit = 0 }),
                "/v3/macro", "limit=0"),
            (() => client.GetMacroPreviewAsync(macro), "/v3/macro/preview", "from=2026-11-06"),
            (() => client.GetMacroSeriesAsync(macro), "/v3/macro/series", "code=US.core_inflation_rate"),
            (() => client.GetMacroEstimateAsync(macro, "parquet"), "/v3/macro/estimate", "format=parquet"),
            (() => client.GetReferenceCatalogueAsync("company_profiles", limit: 0), "/v3/reference", "limit=0"),
            (() => client.GetReferencePreviewAsync(reference), "/v3/reference/preview", "q=Berkshire%20%26%20Co"),
            (() => client.GetReferenceEstimateAsync(reference), "/v3/reference/estimate", "to=2026-11-06")
        ];
        foreach (var (run, path, query) in cases)
        {
            using var result = await run();
            Assert.Equal(path, handler.Uri!.AbsolutePath);
            Assert.Contains(query, handler.Uri.Query);
            Assert.True(result.RootElement.GetProperty("fixture").GetBoolean());
        }
    }

    [Fact]
    public async Task DownloadFamiliesShareChecksumAndPreserveScopedFilters()
    {
        using var handler = new Capture { File = true };
        using var client = new LseDataClient(new() { ApiKey = "fixture-key" }, handler);
        var date = new DateOnly(2026, 11, 6);
        var market = new LseMarketSelection("us_options")
            { Underlying = "SPXW", OptionType = "put", FromDate = date, ToDate = date, AsPublished = true };
        var macro = new LseMacroSelection("US.inflation_rate", From: date);
        (Func<Task<LseExport>> Run, string Path, string Query)[] cases =
        [
            (() => client.ExportAsync(market), "/v3/export", "as_published=1"),
            (() => client.ExportCandlesAliasAsync(market), "/v3/candles", "underlying=SPXW"),
            (() => client.ExportSliceAsync(market, "bars", 300), "/v3/slice", "bar_seconds=300"),
            (() => client.ExportDatasetAsync(market, new() { Indicators = ["rsi:14", "sma:20"], CompanionPairId = 123,
                TimeZone = "America/New_York", DaysOfWeek = ["monday", "tuesday"] }), "/v3/dataset/export", "indicators=rsi%3A14%2Csma%3A20"),
            (() => client.ExportMacroAsync(macro), "/v3/macro/export", "from=2026-11-06"),
            (() => client.ExportMacroValuesAsync(macro), "/v3/macro/values", "code=US.inflation_rate"),
            (() => client.ExportMacroChartAsync(["US.inflation_rate", "US.real_gdp"], date, date, "yoy"),
                "/v3/macro/chart/export", "codes=US.inflation_rate%2CUS.real_gdp"),
            (() => client.ExportReferenceAsync(new("cot", Country: "US")), "/v3/reference/export", "country=US")
        ];
        foreach (var (run, path, query) in cases)
        {
            var file = await run();
            Assert.Equal(path, handler.Uri!.AbsolutePath);
            Assert.Contains(query, handler.Uri.Query);
            Assert.True(file.ChecksumVerified);
            Assert.Equal("fixture-export", file.ExportId);
            Assert.False(file.Adjusted);
        }
    }

    [Fact]
    public async Task PhotoAndExchangeCatalogueUseTheCorrectOriginsAndMetadata()
    {
        using var photoHandler = new Capture { File = true };
        using var client = new LseDataClient(new() { ApiKey = "fixture-key" }, photoHandler);
        var photo = await client.GetInstrumentImageAsync("/v3/photo/company/412/light?v=abc");
        Assert.Equal("\"fixture-etag\"", photo.ETag);
        Assert.Equal("image/webp", photo.ContentType);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetInstrumentImageAsync("//elsewhere/v3/photo/company/412/light"));

        using var catalogueHandler = new Capture();
        using var catalogue = new LseExchangeCatalogue("fixture-key", catalogueHandler);
        using var symbols = await catalogue.GetCmeSymbolsAsync(LseBookLevel.BestBidOffer);
        Assert.Equal("https://cme.londonstrategicedge.com/v1/symbols?level=1", catalogueHandler.Uri!.AbsoluteUri);
    }

    private sealed class Capture : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public bool File { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Assert.Equal("fixture-key", Assert.Single(request.Headers.GetValues("X-API-Key")));
            var bytes = """{"fixture":true}"""u8.ToArray();
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (File)
            {
                reply.Headers.Add("x-checksum-sha256", Convert.ToHexString(SHA256.HashData(bytes)));
                reply.Headers.Add("x-export-id", "fixture-export");
                reply.Headers.Add("x-lse3-adjusted", "0");
                reply.Headers.ETag = new EntityTagHeaderValue("\"fixture-etag\"");
                reply.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
            }
            return Task.FromResult(reply);
        }
    }
}

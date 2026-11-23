using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

internal sealed class DemoRestHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (body, type) = request.RequestUri!.AbsolutePath switch
        {
            "/v3/health" => ("""{"status":"ok","demo":true}""", "application/json"),
            "/v3/usage" => ("""{"tier":"demo","calls_remaining":60,"downloads_remaining":10}""", "application/json"),
            "/v3/markets" => ("""{"markets":[{"folder":"us_stocks","name":"US stocks"}]}""", "application/json"),
            "/v3/pairs" => ("""{"total":1,"pairs":[{"folder":"us_stocks","symbol":"AAPL","name":"Apple Inc.","pair_id":1}]}""", "application/json"),
            "/v3/estimate" => ("""{"rows":2,"bytes":125,"demo":true}""", "application/json"),
            "/v3/overview" => ("""{"stats":{"stored_rows":2},"demo":true}""", "application/json"),
            "/v3/factors" => ("""{"market":"us_stocks","symbol":"AAPL","splits":[],"dividends":{"status":"none"}}""", "application/json"),
            "/v3/preview" => ("""{"columns":["time","close"],"rows":[{"time":"2026-10-23 13:30:00","close":121}]}""", "application/json"),
            "/v3/options/chain" => ("""{"contracts":1,"rows":[{"symbol":"AAPL  261120C00100000","strike":100,"iv":0.23}]}""", "application/json"),
            "/v3/options/flow" => ("""{"rows":[{"symbol":"AAPL  261120C00100000","premium":125000}]}""", "application/json"),
            "/v3/macro" => ("""{"series":[{"code":"US.inflation_rate","name":"Inflation Rate"}]}""", "application/json"),
            "/v3/macro/preview" => ("""{"code":"US.inflation_rate","rows":[{"day":"2026-10-01","value":2.4}]}""", "application/json"),
            "/v3/reference" => ("""{"datasets":[{"dataset":"company_profiles"}]}""", "application/json"),
            "/v3/reference/preview" => ("""{"dataset":"company_profiles","rows":[{"symbol":"AAPL","name":"Apple Inc."}]}""", "application/json"),
            "/v3/export" when request.RequestUri.Query.Contains("timeframe=tick", StringComparison.Ordinal) =>
                ("time,price,size,kind\n2026-10-23 13:30:01.123456789,121,80,trade\n2026-10-23 13:30:02,121.25,10,trade\n", "text/csv"),
            "/v3/export" => ("time,open,high,low,close,volume,trades\n2026-10-22,120,123,118,121,1250,91\n2026-10-23,121,125,120,124,1450,110\n", "text/csv"),
            _ => ("""{"code":"not-found","message":"Demo route unavailable"}""", "application/problem+json")
        };
        var bytes = Encoding.UTF8.GetBytes(body);
        var response = new HttpResponseMessage(type == "application/problem+json" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        if (type == "text/csv")
        {
            response.Headers.Add("x-checksum-sha256", Convert.ToHexStringLower(SHA256.HashData(bytes)));
            response.Headers.Add("x-export-rows", "2");
        }
        return Task.FromResult(response);
    }
}

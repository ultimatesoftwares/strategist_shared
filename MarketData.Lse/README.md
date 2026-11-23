# LSE.NET

A standalone .NET 10 SDK for [London Strategic Edge Data 3.0](https://londonstrategicedge.com/data/#api-docs). It covers the documented Databank HTTP routes, the four standard live price feeds, and the paid CME Group and Nasdaq TotalView exchange protocols. It is an independent client, not an official vendor package.

## Get started

Build from source with the .NET 10 SDK or newer:

```shell
dotnet build LSE.NET.slnx -c Release
dotnet test LSE.NET.slnx -c Release
```

In a consumer project, reference `LSE.NET.csproj`, or build and install the preview NuGet package from a local feed (see [Packaging](#packaging)). Set `LSE_API_KEY` through your shell or secret manager. The library takes the key explicitly and does not read `.env` files.

### Python-style client

`LseClient` is the .NET counterpart to [`lse.LSE`](https://github.com/londonstrategicedge/lse-data). It uses the same key for history, research data, and streaming. It reads `LSE_API_KEY` when no key is passed.

```csharp
using LSE.NET;

using var client = new LseClient();
var candles = await client.CandlesAsync("AAPL", "1h",
    start: new DateOnly(2026, 10, 1), end: new DateOnly(2026, 10, 14));
var cpi = await client.SeriesAsync("US.core_inflation_rate");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
await foreach (var tick in client.StreamAsync(["AAPL", "BTC/USD"], stop.Token))
    Console.WriteLine($"{tick.Symbol} {tick.Price}");
```

| Python SDK | .NET facade |
|---|---|
| `candles`, `option_candles`, `history`, `rows`, `export` | `CandlesAsync`, `OptionCandlesAsync`, `HistoryAsync`, `RowsAsync`, `ExportAsync` |
| `pair_page`, `iter_pairs`, `pairs`, `catalog`, `datasets` | `PairPageAsync`, `IterPairsAsync`, `PairsAsync`, `CatalogAsync`, `DatasetsAsync` |
| `options`, `options_chain`, `options_flow`, `options_underlyings` | `OptionsAsync`, `OptionsChainAsync`, `OptionsFlowAsync`, `OptionsUnderlyingsAsync` |
| `series`, `macro_*`, `reference_*`, `dataset`, `company_profiles`, `dividends`, etc. | `SeriesAsync`, `Macro*Async`, `Reference*Async`, `DatasetAsync`, `CompanyProfilesAsync`, `DividendsAsync`, etc. |
| `stream_async`, `connect_async`, `subscribe`, `unsubscribe` | `StreamAsync`, `ConnectAsync` with the `Tick` event, `SubscribeAsync`, `UnsubscribeAsync` |

The facade uses .NET types: `DateOnly` for inclusive calendar bounds, `LseDateRange` for half-open timestamp windows, typed candles and ticks, and async enumerables for paging and streaming. Generic CSV row methods preserve values as strings (empty fields become `null`). File methods return a path, honor the HTTP export buffer limit, and refuse to overwrite existing files. They do not produce pandas-style DataFrames. For an exact candle window, pass bounds explicitly; the default latest-candle lookback is bounded.

By default, catalogue-listed crypto is routed to London and other symbols to the data socket; a `streamEndpoint` override uses only that socket. Start one live session per client, and use `StreamMessage` to inspect authentication, acknowledgements, and command errors. `Disconnect()` signals that session to stop. Reconnect is caller-managed. The existing `LseDataClient`, `LseStreamClient`, and paid-exchange clients remain available; use `client.Http` to access the underlying HTTP client.

### Low-level HTTP: look up an instrument and fetch candles

```csharp
using LSE.NET;

var key = Environment.GetEnvironmentVariable("LSE_API_KEY")
    ?? throw new InvalidOperationException("Set LSE_API_KEY.");
using var client = new LseDataClient(new LseOptions { ApiKey = key });
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var ct = deadline.Token;

var pair = await client.FindPairAsync("AAPL", "us_stocks", ct);
if (pair is null)
    return;

var end = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
var range = new LseDateRange(end.AddDays(-7), end);
using var estimate = await client.GetEstimateAsync(pair.Market, pair.Symbol, "1d", range, ct: ct);
Console.WriteLine(estimate.RootElement);

var candles = await client.GetCandlesAsync(pair.Market, pair.Symbol, "1d", range, ct);
foreach (var bar in candles)
    Console.WriteLine($"{bar.Time:O} {bar.Open} {bar.High} {bar.Low} {bar.Close} {bar.Volume}");
```

| Method | Result |
|---|---|
| `GetHealthAsync`, `GetUsageAsync`, `GetMarketsAsync` | Service status, key usage and limits, and market catalogue. |
| `SearchPairsAsync` | One catalogue page, with optional market, search text, limit, and offset. For specialized filters or cursors, use `GetJsonAsync`. |
| `FindPairAsync` | The first exact match or `null` for an empty result. Pass a market to disambiguate symbols across venues. |
| `GetEstimateAsync` | Vendor estimate and quota information; does not download the file. |
| `GetCandlesAsync`, `GetTradesAsync` | Typed historical rows. Candles use CSV, and trades default to Parquet (`TickFormat = "csv"` opts into CSV). |
| `ExportAsync` | File bytes, a suggested base filename, charged row count (`-1` if unknown), format, and checksum verification status. |
| `GetJsonAsync` | Escape hatch for other documented JSON GET endpoints, e.g. `/v3/overview?market=us_stocks&symbol=AAPL`. Supply only an origin-relative `/v3/…` path and URI-escape query values. |

Use the [HTTP reference](https://londonstrategicedge.com/api-documentation) for endpoint-specific instruments, entitlement rules, filters, and limits. Typed trade history is the standard `timeframe=tick` print slice, not quote data or a typed decoder for paid exchange schemas. Typed methods return an empty collection only for a documented empty selection; unsupported datasets still throw. `LseDateRange` is **half-open** (`From` included, `To` excluded); `DateTimeOffset` bounds are converted to UTC while retaining their fractional seconds. Prices are `double`; raw exports remain available when exact source representations matter.

**Ownership and limits.** Reuse one `LseDataClient` per key and dispose it at shutdown. It owns any supplied handler and snapshots its options. Dispose JSON results (`JsonDocument`); typed rows and export bytes need no disposal. Defaults are 60 requests per minute and one export at a time, **per client**, not per key across processes. The vendor also enforces download and row quotas. The client buffers complete responses: 8 MiB for JSON and 128 MiB for exports by default, configurable via `MaxJsonBytes` and `MaxExportBytes`. Peak memory can be higher while buffering and decoding; estimate first and split large ranges.

**Failure handling.** `LseException` carries `Status`, `Code`, `RetryAfter`, and optional `RequiredPlanJson`. HTTP status `0` indicates a client-side failure. Transient transport failures, interrupted transfers, checksum mismatches, and 429/500/502/503/504 responses share one retry budget (`Retries`, default 3), with jittered backoff. A positive `Retry-After` on 429 or 503 of up to two minutes can be honored once outside that budget (`WaitForLimit`); otherwise the refusal is returned to the caller. A retry can consume additional quota. `TimeoutSeconds` (default 60) bounds the header phase and individual body reads, not the entire operation: use a cancellation token for a total deadline. `ChecksumVerified` is false when a checksum was not supplied. Treat filenames as hints, not trusted output paths.

**Credentials.** The HTTP key is sent only in `X-API-Key`, and the default handler refuses redirects. If you inject a custom handler or override an endpoint, you are responsible for trusting that transport and destination. Use token cancellation rather than logging request payloads or raw replies. Do not log your key or commit it to source.

### WebSocket: subscribe and read live prices

```csharp
using LSE.NET;

var key = Environment.GetEnvironmentVariable("LSE_API_KEY")
    ?? throw new InvalidOperationException("Set LSE_API_KEY.");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

await using var feed = await LseStreamClient.ConnectAsync(new()
{
    ApiKey = key,
    Endpoint = LseStreamEndpoints.London
}, stop.Token);

await feed.SubscribeAsync("BTC/USD", stop.Token);
try
{
    await foreach (var update in feed.ReadMessagesAsync(stop.Token))
    {
        if (update.GetTick() is { } tick)
            Console.WriteLine($"{tick.Time:O} {tick.Symbol} {tick.Price}");
        else if (update.Type == "error")
            Console.Error.WriteLine("The server refused a stream command.");
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
```

| Endpoint | Example symbols |
|---|---|
| `LseStreamEndpoints.Data` | `AAPL`, `SPY`, `EUR/USD`, `XAU/USD`; also supports US option chains. |
| `LseStreamEndpoints.Chicago` | `SP500.F`, `EUR/USD` and other major pairs. |
| `LseStreamEndpoints.London` | `VOD.L`, `BTC/USD`. |
| `LseStreamEndpoints.Toronto` | `RY.TO`. |

Use the [WebSocket reference](https://londonstrategicedge.com/websocket-documentation) for the full symbol and message reference. Socket symbols are not necessarily HTTP symbols (`BTC/USD` vs. `BTCUSDT`). A subscription on the wrong socket can be accepted without ticks. A closed market may send only the last price. For paid CME or Nasdaq market depth, use `LseExchangeStream`.

`ConnectAsync` waits for `welcome` and `authenticated`; inspect `Authentication.Payload` for the vendor's plan and initial catalogue. Start exactly one `ReadMessagesAsync` loop promptly and keep it draining. `SubscribeAsync`, `UnsubscribeAsync`, `SubscribeOptionsAsync`, `UnsubscribeOptionsAsync`, `ListSymbolsAsync`, and `PingAsync` send commands; completing one does not mean the vendor accepted it. Check acknowledgements and `error` messages in the read loop. Unknown message types and extra fields remain available in `Payload`. `GetTick()` projects prices, UTC time and optional metadata without losing a supplied integer `ts_ns` or sequence number. Optional fields are `null` when not sent.

The client serializes sends and sends an application heartbeat every 25 seconds by default. Frames can be fragmented; the default maximum assembled message is 32 MiB to accommodate catalogues. There is no background tick queue, no automatic reconnect, and no market-data replay guarantee. On a normal close the iterator ends; on abnormal close or invalid protocol it can throw. Disposing or leaving the iterator closes the connection. For production recovery, reconnect with bounded backoff on transient failures, then reapply acknowledged subscriptions; do not loop on a bad key or plan refusal. Treat feed sequence gaps according to your application's recovery policy.

## API coverage

The table maps the [HTTP reference](https://londonstrategicedge.com/api-documentation) and [WebSocket reference](https://londonstrategicedge.com/websocket-documentation) to SDK entry points. JSON methods preserve vendor fields and return caller-owned `JsonDocument` instances. Exports preserve native schemas in CSV or Parquet; only the standard candle and trade convenience methods project rows to SDK records.

| Documented routes or protocol | SDK entry points |
|---|---|
| `/v3/health`, `/v3/usage`, `/v3/markets` | `GetHealthAsync`, `GetUsageAsync`, `GetMarketsAsync` |
| `/v3/pairs`, `/v3/catalogue` | `SearchPairsAsync(LsePairSearch)`, `FindPairAsync`, `GetCatalogueAsync`; includes cursor, grouping and contract filters |
| `/v3/factors`, `/v3/tables`, `/v3/overview`, `/v3/photo/{kind}/{key}/{theme}` | `GetFactorsAsync`, `GetTablesAsync`, `GetOverviewAsync`, `GetInstrumentImageAsync` |
| `/v3/preview`, `/v3/estimate`, `/v3/export`, `/v3/candles`, `/v3/slice` | `GetPreviewAsync`, `GetEstimateAsync`, `ExportAsync`, `ExportCandlesAliasAsync`, `ExportSliceAsync`; supports symbol, pair ID, or option underlying and candles, trades, quotes, books, orders, or Greeks where entitled |
| `/v3/dataset/export` | `ExportDatasetAsync`; indicators, companion instrument and features, correlation window, and local-time filters |
| `/v3/options/chain`, `/v3/options/flow` | `GetOptionChainAsync`, `GetOptionFlowAsync` |
| `/v3/macro`, `/v3/macro/preview`, `/v3/macro/estimate`, `/v3/macro/series` | `SearchMacroAsync`, `GetMacroPreviewAsync`, `GetMacroEstimateAsync`, `GetMacroSeriesAsync` |
| `/v3/macro/export`, `/v3/macro/values`, `/v3/macro/chart/export` | `ExportMacroAsync`, `ExportMacroValuesAsync`, `ExportMacroChartAsync`; observations, releases, yields, and multi-series transformations |
| `/v3/reference`, `/v3/reference/preview`, `/v3/reference/estimate`, `/v3/reference/export` | `GetReferenceCatalogueAsync`, `GetReferencePreviewAsync`, `GetReferenceEstimateAsync`, `ExportReferenceAsync`; all vendor collections by dataset ID |
| Standard LSE price sockets | `LseStreamClient`: authentication, subscriptions, option chains, symbol lists, and heartbeats |
| Paid CME and Nasdaq `/v1/stream`; CME `/v1/symbols` | `LseExchangeStream`, `LseExchangeCatalogue.GetCmeSymbolsAsync` |

Use `LseMarketSelection` for market exports that need a pair ID, underlying, or a calendar-day selection. `FromDate` and `ToDate` are inclusive and can be the same day. `Window` uses timestamps and excludes the upper bound; don't mix the two forms. Omitting both bounds on preview or estimate uses the vendor's latest window. Macro and reference date filters are inclusive.

The vendor retired prepared jobs and resumable export files on November 6, 2026. `/v3/jobs/{id}` and its `/file` subroute now return `404 export-unknown`; `/v3/files` and `/v3/file/...` return `410 export-required`. The SDK does not expose a job workflow for retired routes. `prepare=1` no longer changes the export response.

### Paid exchange streams

```csharp
using LSE.NET;

var key = Environment.GetEnvironmentVariable("LSE_API_KEY")
    ?? throw new InvalidOperationException("Set an entitled exchange key.");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

using var catalogue = new LseExchangeCatalogue(key);
using var symbols = await catalogue.GetCmeSymbolsAsync(LseBookLevel.BestBidOffer, stop.Token);

await using var exchange = await LseExchangeStream.ConnectAsync(
    new() { ApiKey = key, Exchange = LseExchange.Cme },
    new(["ESZ6"], LseBookLevel.BestBidOffer, LseExchangeEncoding.Json),
    stop.Token);

await foreach (var message in exchange.ReadMessagesAsync(stop.Token))
{
    if (message is LseExchangeJsonMessage { Type: "mbp1" } tick &&
        tick.Record is { } record && record.GetProperty("price").GetString() is { } price)
        Console.WriteLine($"{tick.Symbol} seq={tick.Sequence} price={LseExchangeJsonMessage.ParsePrice(price)}");
}
```

Select `LseExchange.Nasdaq` and use stock symbols for Nasdaq TotalView. For both feeds, level 1 is best bid/offer and trades, level 2 is price depth, and level 3 is order events. The key is sent in the upgrade header, never in the query, and each connection accepts exactly one subscription. `Hello` exposes the feed epoch, and snapshot markers and live records arrive in `ReadMessagesAsync`. A sequence jump indicates missed records; reconnect for a fresh snapshot. Abnormal closures include `LseException.WebSocketCloseStatus`.

With `LseExchangeEncoding.Dbn`, data messages are `LseExchangeDbnBatch` values with a validated L3DB envelope, instrument ID, sequence, and individual record slices. The SDK does not deserialize the interior DBN fields. JSON mode preserves the full record, and `ParsePrice` converts integer billionths to `decimal` without treating the no-price sentinel as a price.

## Terminal example

```shell
# Interactive dashboard with fabricated quotes and a local HTTP fixture
dotnet run --project examples/Tui -- --demo

# Smoke test without a terminal, credentials, or network access
dotnet run --project examples/Tui -- --demo --once

# Live dashboard (requires LSE_API_KEY)
dotnet run --project examples/Tui
```

In the terminal, try `connect data`, `sub AAPL SPY`, `find us_stocks apple`, `usage`, `estimate us_stocks AAPL 1d`, and `candles us_stocks AAPL 1d`. The last two default to the preceding seven UTC dates; append `2026-10-19 2026-10-24` for an explicit half-open window. Try `trades us_stocks AAPL 2026-10-23T13:30:00Z 2026-10-23T13:31:00Z` for a short print slice. `options`, `unoptions`, `unsub`, and `symbols` exercise the other feed commands. `help` lists commands, and `quit`, Escape at an empty prompt, or Ctrl+C exits. The watch panel shows up to twelve recently updated instruments, not every subscription or event. The interactive dashboard requires an ANSI-capable terminal.

Explore the additional families with `chain AAPL`, `flow AAPL`, `macro observation US inflation`, `series observation US.inflation_rate`, and `reference company_profiles AAPL`. To watch paid order-book messages, set an entitled key and enter `exchange cme 2 ESZ6` or `exchange nasdaq 3 NVDA`.

## Packaging

```shell
dotnet pack LSE.NET.csproj -c Release -o artifacts
dotnet add path/to/consumer.csproj package LSE.NET \
  --version 0.1.0-preview.2 --source /absolute/path/to/artifacts
```

For an offline local feed, make the SDK's NuGet dependencies available there too or pre-restore them from your normal feed. The package includes the SDK assembly, XML API docs, and this README; it does not include the example or tests. No package is published by these commands. Before publishing, confirm package ID ownership and set the distribution license and repository metadata for the chosen standalone repository. Repository metadata is not inferred from the current checkout.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LSE.NET;

internal sealed class Dashboard(LseDataClient rest, string key, bool demo, CancellationToken stop) : IAsyncDisposable
{
    private const int WatchLimit = 12;
    private static readonly string[] Help =
    [
        "connect data|chicago|london|toronto    disconnect",
        "sub SYMBOL [SYMBOL ...]                unsub SYMBOL",
        "options UNDERLYING                    unoptions UNDERLYING",
        "symbols [options|all]                 ping",
        "health                                usage",
        "markets                               find MARKET QUERY",
        "candles MARKET SYMBOL TIMEFRAME FROM TO",
        "estimate MARKET SYMBOL TIMEFRAME FROM TO",
        "trades MARKET SYMBOL FROM TO          (use a short window)",
        "overview                              factors MARKET SYMBOL",
        "preview MARKET SYMBOL TIMEFRAME [DATA]",
        "chain ROOT                            flow ROOT",
        "macro KIND COUNTRY QUERY              series KIND CODE",
        "reference DATASET [SYMBOL]            refs",
        "exchange cme|nasdaq LEVEL SYMBOL [SYMBOL ...] (paid key)",
        "FROM and TO are UTC dates or ISO timestamps; TO is exclusive.",
        "Esc (on an empty prompt), quit or Ctrl+C exits."
    ];

    private readonly ConcurrentDictionary<string, LseLiveTick> _ticks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _tickLock = new();
    private CancellationTokenSource? _feedStop;
    private Task? _feedReader;
    private LseStreamClient? _feed;
    private LseExchangeStream? _exchange;
    private volatile string _feedStatus = "Disconnected";
    private string[] _result = Help;

    public async Task RunOnceAsync()
    {
        using var health = await rest.GetHealthAsync(stop);
        Console.WriteLine(Safe("Health: " + health.RootElement.GetRawText(), 1000));
        var (market, symbol, frame, range) = Selection(["candles", "us_stocks", "AAPL", "1d"]);
        var candles = await rest.GetCandlesAsync(market, symbol, frame, range, stop);
        foreach (var line in CandleLines(candles))
            Console.WriteLine(line);
    }

    public async Task RunAsync()
    {
        var input = new StringBuilder();
        Task? pending = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(125));
        Console.Write("\u001b[?1049h\u001b[?25l");
        try
        {
            if (demo)
                pending = CommandAsync("connect data");
            do
            {
                if (pending?.IsCompleted == true)
                {
                    await pending;
                    pending = null;
                }
                while (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(intercept: true);
                    if (k.Key == ConsoleKey.Escape && input.Length == 0 && pending is null)
                        return;
                    if (k.Key == ConsoleKey.Enter && pending is null)
                    {
                        var command = input.ToString().Trim();
                        input.Clear();
                        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                            return;
                        if (command.Length > 0)
                            pending = CommandAsync(command);
                    }
                    else if (k.Key == ConsoleKey.Escape)
                        input.Clear();
                    else if (k.Key == ConsoleKey.Backspace && input.Length > 0)
                        input.Length--;
                    else if (!char.IsControl(k.KeyChar) && input.Length < 300)
                        input.Append(k.KeyChar);
                }
                Render(input.ToString(), pending is not null);
            } while (await timer.WaitForNextTickAsync(stop));
        }
        finally
        {
            Console.Write("\u001b[?25h\u001b[?1049l");
            if (pending is not null)
                await pending;
        }
    }

    private void Render(string input, bool busy)
    {
        var width = Math.Max(1, Console.WindowWidth - 1);
        var height = Math.Max(1, Console.WindowHeight);
        var feedRows = Math.Clamp(height / 4, 1, WatchLimit);
        var lines = new List<string>
        {
            $"LSE.NET / MARKET DATA TERMINAL {(demo ? "[SIMULATED]" : "[LIVE]")}",
            _feedStatus,
            "SYMBOL                  LAST            BID            ASK        SIZE    SOURCE UTC"
        };
        var ticks = _ticks.Values.OrderByDescending(t => t.Time).Take(feedRows).ToArray();
        lines.AddRange(ticks.Select(t => $"{Safe(t.Symbol, 18),-18} {t.Price,11:G9} {t.Bid,14:G9} {t.Ask,14:G9} {t.Volume,11:G7}    {t.Time:HH:mm:ss.fff}"));
        for (var i = ticks.Length; i < feedRows; i++)
            lines.Add(i == 0 ? "(no ticks yet)" : "");
        lines.Add(busy ? "REQUEST IN PROGRESS (Ctrl+C to cancel)" : "RESULT / enter help for commands");
        lines.AddRange(Volatile.Read(ref _result).Take(Math.Max(0, height - lines.Count - 1)));
        var frame = new StringBuilder("\u001b[H\u001b[2J");
        foreach (var line in lines.Take(height - 1))
            frame.Append(Safe(line, width)).Append('\n');
        frame.Append(Safe("> " + input, width));
        Console.Write(frame.ToString());
    }

    private async Task CommandAsync(string command)
    {
        try
        {
            var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = await ExecuteAsync(args);
            Volatile.Write(ref _result, result);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e) when (e is ArgumentException or FormatException or LseException or WebSocketException or InvalidOperationException)
        {
            Volatile.Write(ref _result, [Error(e)]);
        }
    }

    private async Task<string[]> ExecuteAsync(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "help": return Help;
            case "connect" when args.Length == 2:
                await ConnectAsync(args[1]);
                return ["Connected. Use sub SYMBOL to start watching."];
            case "disconnect":
                await DisconnectAsync();
                return ["Disconnected."];
            case "sub" when args.Length is >= 2 and <= WatchLimit + 1:
                foreach (var symbol in args[1..])
                    if (!demo) await Feed().SubscribeAsync(symbol, stop);
                    else DemoTick(symbol);
                return ["Subscription commands sent. See the feed status for acknowledgements."];
            case "unsub" when args.Length == 2:
                if (!demo) await Feed().UnsubscribeAsync(args[1], stop);
                _ticks.TryRemove(args[1], out _);
                return ["Unsubscribe sent."];
            case "options" when args.Length == 2 && !demo:
                await Feed().SubscribeOptionsAsync(args[1], stop);
                return ["Option-chain subscription sent."];
            case "unoptions" when args.Length == 2 && !demo:
                await Feed().UnsubscribeOptionsAsync(args[1], stop);
                return ["Option-chain unsubscribe sent."];
            case "symbols" when args.Length <= 2 && !demo:
                await Feed().ListSymbolsAsync(args.Length == 2 ? args[1] : null, stop);
                return ["Catalogue requested. See feed status for the count."];
            case "ping" when !demo:
                await Feed().PingAsync(stop);
                return ["Ping sent."];
            case "health": return await JsonLinesAsync(rest.GetHealthAsync(stop));
            case "usage": return await JsonLinesAsync(rest.GetUsageAsync(stop));
            case "markets": return await JsonLinesAsync(rest.GetMarketsAsync(stop));
            case "overview": return await JsonLinesAsync(rest.GetOverviewAsync(ct: stop));
            case "factors" when args.Length == 3:
                return await JsonLinesAsync(rest.GetFactorsAsync(args[1], args[2], ct: stop));
            case "preview" when args.Length is 4 or 5:
                return await JsonLinesAsync(rest.GetPreviewAsync(new(args[1])
                    { Symbol = args[2], Timeframe = args[3], Data = args.Length == 5 ? args[4] : null }, stop));
            case "chain" when args.Length == 2:
                return await JsonLinesAsync(rest.GetOptionChainAsync(new(args[1]) { Limit = 5 }, stop));
            case "flow" when args.Length == 2:
                return await JsonLinesAsync(rest.GetOptionFlowAsync(new() { Underlying = args[1], Limit = 5 }, stop));
            case "macro" when args.Length >= 4:
                return await JsonLinesAsync(rest.SearchMacroAsync(new()
                    { Kind = args[1], Country = args[2], Query = string.Join(' ', args[3..]), Limit = 5 }, stop));
            case "series" when args.Length == 3:
                return await JsonLinesAsync(rest.GetMacroPreviewAsync(new(args[2], args[1]), stop));
            case "refs":
                return await JsonLinesAsync(rest.GetReferenceCatalogueAsync(ct: stop));
            case "reference" when args.Length is 2 or 3:
                return await JsonLinesAsync(rest.GetReferencePreviewAsync(new(args[1], args.Length == 3 ? args[2] : null), stop));
            case "exchange" when args.Length >= 4 && !demo:
                await ConnectExchangeAsync(args);
                return ["Exchange stream subscribed. Sequence and book/order updates appear in feed status."];
            case "find" when args.Length >= 3:
                return await PairsAsync(args[1], string.Join(' ', args[2..]));
            case "candles" when args.Length is 4 or 6:
            {
                var (market, symbol, frame, range) = Selection(args);
                return CandleLines(await rest.GetCandlesAsync(market, symbol, frame, range, stop));
            }
            case "estimate" when args.Length is 4 or 6:
            {
                var (market, symbol, frame, range) = Selection(args);
                return await JsonLinesAsync(rest.GetEstimateAsync(market, symbol, frame, range, ct: stop));
            }
            case "trades" when args.Length == 5:
            {
                var range = new LseDateRange(ParseTime(args[3]), ParseTime(args[4]));
                var trades = await rest.GetTradesAsync(args[1], args[2], range, stop);
                return [$"{trades.Count} trades (latest 8 shown)", "UTC                                    PRICE           SIZE KIND",
                    .. trades.TakeLast(8).Select(t => $"{t.Time:yyyy-MM-dd HH:mm:ss.fffffff} {t.Price,15:G9} {t.Size,14:G7} {t.Kind}")];
            }
            default: return ["Unknown or unavailable command. Enter help for usage."];
        }
    }

    private static (string Market, string Symbol, string Frame, LseDateRange Range) Selection(string[] args)
    {
        var end = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        if (args.Length == 6)
            return (args[1], args[2], args[3], new(ParseTime(args[4]), ParseTime(args[5])));
        return (args[1], args[2], args[3], new(end.AddDays(-7), end));
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static async Task<string[]> JsonLinesAsync(Task<JsonDocument> request)
    {
        using var doc = await request;
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })
            .Split('\n').Take(80).ToArray();
    }

    private async Task<string[]> PairsAsync(string market, string query)
    {
        using var doc = await rest.SearchPairsAsync(market, query, limit: 10, ct: stop);
        if (!doc.RootElement.TryGetProperty("pairs", out var pairs) || pairs.ValueKind != JsonValueKind.Array)
            throw new LseException(0, "invalid-response", "The catalogue did not return a pairs array.");
        var rows = pairs.EnumerateArray().Select(p =>
            $"{Label(p, "folder"),-14} {Label(p, "symbol"),-18} {Label(p, "name")}").ToArray();
        return rows.Length == 0 ? ["No matching instruments."] : rows;
    }

    private static string[] CandleLines(IReadOnlyList<LseCandle> candles) =>
        [$"{candles.Count} candles (latest 8 shown)",
         "UTC                       OPEN       HIGH        LOW      CLOSE       VOLUME",
         .. candles.TakeLast(8).Select(c => $"{c.Time:yyyy-MM-dd HH:mm:ss} {c.Open,10:G7} {c.High,10:G7} {c.Low,10:G7} {c.Close,10:G7} {c.Volume,12:G8}")];

    private async Task ConnectAsync(string region)
    {
        var endpoint = region.ToLowerInvariant() switch
        {
            "data" => LseStreamEndpoints.Data,
            "chicago" => LseStreamEndpoints.Chicago,
            "london" => LseStreamEndpoints.London,
            "toronto" => LseStreamEndpoints.Toronto,
            _ => throw new ArgumentException("Expected data, chicago, london or toronto.")
        };
        await DisconnectAsync();
        if (demo)
        {
            _feedStatus = "Simulated feed (subscriptions generate sample prices).";
            DemoTick("AAPL");
            DemoTick("SPY");
            _feedStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _feedReader = DemoFeedAsync(_feedStop.Token);
            return;
        }
        _feedStatus = "Connecting to " + endpoint.Host + "...";
        try
        {
            _feed = await LseStreamClient.ConnectAsync(new() { ApiKey = key, Endpoint = endpoint }, stop);
        }
        catch (Exception e) when (e is LseException or WebSocketException or OperationCanceledException)
        {
            _feedStatus = "Disconnected";
            throw;
        }
        _feedStatus = $"Connected to {endpoint.Host} / {Label(_feed.Authentication.Payload, "tier")}";
        _feedStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _feedReader = ReadFeedAsync(_feed, _feedStop.Token);
    }

    private async Task ConnectExchangeAsync(string[] args)
    {
        var exchange = args[1].ToLowerInvariant() switch
        {
            "cme" => LseExchange.Cme, "nasdaq" => LseExchange.Nasdaq,
            _ => throw new ArgumentException("Expected cme or nasdaq.")
        };
        if (!int.TryParse(args[2], out var level) || level is < 1 or > 3)
            throw new ArgumentException("Expected exchange depth 1, 2, or 3.");
        await DisconnectAsync();
        _exchange = await LseExchangeStream.ConnectAsync(new() { ApiKey = key, Exchange = exchange },
            new(args[3..], (LseBookLevel)level, LseExchangeEncoding.Json), stop);
        _feedStatus = $"{exchange} Level {level} / epoch {_exchange.Hello.Epoch}";
        _feedStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _feedReader = ReadExchangeAsync(_exchange, _feedStop.Token);
    }

    private async Task ReadExchangeAsync(LseExchangeStream stream, CancellationToken ct)
    {
        try
        {
            await foreach (var message in stream.ReadMessagesAsync(ct))
            {
                if (message is not LseExchangeJsonMessage json)
                    continue;
                var price = json.Record is { } record && record.ValueKind == JsonValueKind.Object
                    && record.TryGetProperty("price", out var value) && value.ValueKind == JsonValueKind.String
                    ? LseExchangeJsonMessage.ParsePrice(value.GetString()!) : null;
                _feedStatus = $"{json.Type} {json.Symbol} seq={json.Sequence} price={price}";
            }
            _feedStatus = "Exchange feed closed.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) when (e is LseException or WebSocketException or FormatException or OverflowException)
        {
            _feedStatus = Error(e);
        }
    }

    private async Task ReadFeedAsync(LseStreamClient feed, CancellationToken ct)
    {
        try
        {
            await foreach (var update in feed.ReadMessagesAsync(ct))
            {
                if (update.GetTick() is { } tick)
                    Track(tick);
                else if (update.Type == "error")
                    _feedStatus = $"{Label(update.Payload, "code")}: {Label(update.Payload, "message")}";
                else if (update.Type == "symbols")
                    _feedStatus = update.Payload.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array
                        ? $"Catalogue returned {symbols.GetArrayLength()} entries."
                        : "Catalogue response received.";
                else if (update.Type != "pong")
                    _feedStatus = $"{update.Type}: {Label(update.Payload, "symbol")} {Label(update.Payload, "underlying")}";
            }
            _feedStatus = "Feed closed. Use connect and resubscribe.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) when (e is LseException or WebSocketException)
        {
            _feedStatus = Error(e) + " Use connect and resubscribe.";
        }
    }

    private void Track(LseLiveTick tick)
    {
        lock (_tickLock)
        {
            if (_ticks.Count >= WatchLimit && !_ticks.ContainsKey(tick.Symbol))
                _ticks.TryRemove(_ticks.Values.MinBy(t => t.Time).Symbol, out _);
            _ticks[tick.Symbol] = tick;
        }
    }

    private void DemoTick(string symbol)
    {
        var price = Math.Round((_ticks.TryGetValue(symbol, out var tick) ? tick.Price : 120) + Random.Shared.NextDouble() - .5, 2);
        Track(new(symbol, DateTime.UtcNow, price, price - .01, price + .01, 10, null, null, null, null, null));
    }

    private async Task DemoFeedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                lock (_tickLock)
                    foreach (var symbol in _ticks.Keys)
                        DemoTick(symbol);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private LseStreamClient Feed() => _feed is { } feed && _feedReader is { IsCompleted: false }
        ? feed : throw new InvalidOperationException("Connect to a feed first.");

    private async Task DisconnectAsync()
    {
        _feedStop?.Cancel();
        if (_feedReader is not null) await _feedReader;
        if (_feed is not null) await _feed.DisposeAsync();
        if (_exchange is not null) await _exchange.DisposeAsync();
        _feedStop?.Dispose();
        _feedStop = null;
        _feed = null;
        _exchange = null;
        _feedReader = null;
        _ticks.Clear();
        _feedStatus = "Disconnected";
    }

    private static string Label(JsonElement json, string field) =>
        json.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    internal static string Safe(string text, int limit) =>
        string.Concat(text.Take(limit).Select(c => c is >= ' ' and <= '~' ? c : '?'));

    internal static string Error(Exception e) => Safe(
        e is LseException lse ? $"{lse.Code} ({lse.Status}): {lse.Message}" : e.Message, 512);

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}

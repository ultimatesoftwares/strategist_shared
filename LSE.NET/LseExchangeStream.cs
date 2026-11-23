using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LSE.NET;

/// <summary>Paid exchange feeds, distinct from the standard LSE live-price sockets.</summary>
public enum LseExchange { Cme, Nasdaq }

/// <summary>Market depth requested from the exchange feed.</summary>
public enum LseBookLevel { BestBidOffer = 1, PriceLevels = 2, Orders = 3 }

/// <summary>Exchange record encoding. Control messages are JSON in either mode.</summary>
public enum LseExchangeEncoding { Json, Dbn }

/// <summary>One exchange subscription. Changing the subscription requires a new connection.</summary>
public sealed record LseExchangeSubscription(IReadOnlyList<string> Symbols,
    LseBookLevel Level = LseBookLevel.Orders, LseExchangeEncoding Encoding = LseExchangeEncoding.Dbn);

/// <summary>Settings for a single paid exchange connection.</summary>
public sealed class LseExchangeStreamOptions
{
    public string ApiKey { get; init; } = "";
    public LseExchange Exchange { get; init; } = LseExchange.Cme;
    /// <summary>Optional trusted stream URI override, e.g. a loopback fixture.</summary>
    public Uri? Endpoint { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxMessageBytes { get; init; } = 32 * 1024 * 1024;
}

/// <summary>Base type for the exchange's JSON messages and binary record batches.</summary>
public abstract record LseExchangeMessage;

/// <summary>A control message or a JSON market-data record.</summary>
public sealed record LseExchangeJsonMessage(string Type, JsonElement Payload) : LseExchangeMessage
{
    public string? Symbol => Payload.TryGetProperty("symbol", out var value) ? value.GetString() : null;
    public ulong? Sequence => Payload.TryGetProperty("sequence", out var value) ? value.GetUInt64() : null;
    public ulong? Epoch => Payload.TryGetProperty("epoch", out var value) ? value.GetUInt64() : null;
    public JsonElement? Record => Payload.TryGetProperty("record", out var value) ? value : null;

    /// <summary>Converts a fixed-point exchange price to a decimal, preserving the no-price sentinel.</summary>
    public static decimal? ParsePrice(string value)
    {
        var units = long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return units == long.MaxValue ? null : units / 1_000_000_000m;
    }
}

/// <summary>A validated L3DB v1 envelope with raw fixed-size DBN records.</summary>
public sealed record LseExchangeDbnBatch : LseExchangeMessage
{
    internal LseExchangeDbnBatch(bool isSnapshot, ulong sequence, uint instrumentId, int count,
        int recordSize, ReadOnlyMemory<byte> records)
    {
        IsSnapshot = isSnapshot;
        Sequence = sequence;
        InstrumentId = instrumentId;
        Count = count;
        RecordSize = recordSize;
        Records = records;
    }

    public bool IsSnapshot { get; }
    public ulong Sequence { get; }
    public uint InstrumentId { get; }
    public int Count { get; }
    public int RecordSize { get; }
    public ReadOnlyMemory<byte> Records { get; }

    /// <summary>Gets the wire bytes for one DBN record without copying them.</summary>
    public ReadOnlyMemory<byte> GetRecord(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Records.Slice(index * RecordSize, RecordSize);
    }
}

/// <summary>One Nasdaq TotalView or CME Group stream with one immutable subscription.</summary>
public sealed class LseExchangeStream : IAsyncDisposable
{
    private static readonly HttpMessageInvoker UpgradeTransport =
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

    private readonly ClientWebSocket _socket;
    private readonly LseBookLevel _level;
    private readonly LseExchangeEncoding _encoding;
    private readonly int _maxMessageBytes;
    private readonly string _key;
    private int _reader;
    private int _disposed;

    private LseExchangeStream(ClientWebSocket socket, LseExchangeSubscription subscription, int limit,
        string key, LseExchangeJsonMessage hello)
    {
        _socket = socket;
        _level = subscription.Level;
        _encoding = subscription.Encoding;
        _maxMessageBytes = limit;
        _key = key;
        Hello = hello;
    }

    /// <summary>The opening control message, including the dataset and feed epoch.</summary>
    public LseExchangeJsonMessage Hello { get; }

    /// <summary>Gets the vendor's exchange WebSocket endpoint.</summary>
    public static Uri GetEndpoint(LseExchange exchange) => exchange switch
    {
        LseExchange.Cme => new("wss://cme.londonstrategicedge.com/v1/stream"),
        LseExchange.Nasdaq => new("wss://nasdaq.londonstrategicedge.com/v1/stream"),
        _ => throw new ArgumentOutOfRangeException(nameof(exchange))
    };

    /// <summary>Authenticates on upgrade and sends a subscription before reading the opening message.</summary>
    public static async Task<LseExchangeStream> ConnectAsync(LseExchangeStreamOptions options,
        LseExchangeSubscription subscription, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(subscription);
        LseOptions.ValidateKey(options.ApiKey);
        ValidateLevel(subscription.Level);
        var encoding = subscription.Encoding switch
        {
            LseExchangeEncoding.Json => "json", LseExchangeEncoding.Dbn => "dbn",
            _ => throw new ArgumentOutOfRangeException(nameof(subscription), "Unknown record encoding.")
        };
        var symbols = subscription.Symbols?.ToArray();
        if (symbols is null || symbols.Length == 0
            || symbols.Any(s => string.IsNullOrWhiteSpace(s) || s != s.Trim() || s != s.ToUpperInvariant()
                || s.Any(char.IsControl))
            || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length)
            throw new ArgumentException("Supply distinct exchange symbols in uppercase.", nameof(subscription));
        if (options.ConnectTimeout <= TimeSpan.Zero || options.ConnectTimeout > TimeSpan.FromHours(1)
            || options.MaxMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid exchange connection limits.");
        var endpoint = options.Endpoint ?? GetEndpoint(options.Exchange);
        if (!LseOptions.IsSecureEndpoint(endpoint, "wss", "ws") || endpoint.Query.Length > 0)
            throw new ArgumentException("Expected a WSS endpoint without a query (WS loopback is allowed).", nameof(options));

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-API-Key", options.ApiKey);
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ConnectTimeout);
        try
        {
            await socket.ConnectAsync(endpoint, UpgradeTransport, timeout.Token).ConfigureAwait(false);
            var message = JsonSerializer.SerializeToUtf8Bytes(new { op = "subscribe", symbols, level = (int)subscription.Level, encoding });
            await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            var frame = await ReadFrameAsync(socket, options.MaxMessageBytes, options.ApiKey, timeout.Token).ConfigureAwait(false);
            if (frame is not { Type: WebSocketMessageType.Text } || ReadJson(frame.Value.Bytes) is not { Type: "hello" } hello)
                throw Protocol("Expected an exchange hello message.");
            if (!hello.Payload.TryGetProperty("level", out var returnedLevel) || returnedLevel.ValueKind != JsonValueKind.Number
                || !returnedLevel.TryGetInt32(out var depth) || depth != (int)subscription.Level
                || !hello.Payload.TryGetProperty("encoding", out var returnedEncoding)
                || returnedEncoding.ValueKind != JsonValueKind.String || returnedEncoding.GetString() != encoding)
                throw Protocol("The exchange hello did not match the requested level and encoding.");
            return new LseExchangeStream(socket, subscription,
                options.MaxMessageBytes, options.ApiKey, hello);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            socket.Dispose();
            throw new LseException(0, "stream-timeout", "The exchange WebSocket handshake timed out.");
        }
        catch (WebSocketException) when ((int)socket.HttpStatusCode >= 300)
        {
            var status = (int)socket.HttpStatusCode;
            socket.Dispose();
            throw new LseException(status, status == 401 ? "key-unknown" : $"http-{status}",
                $"The exchange refused the WebSocket upgrade (HTTP {status}).");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Reads the snapshot and live messages in wire order. Enumerate once.</summary>
    public async IAsyncEnumerable<LseExchangeMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _reader, 1) != 0)
            throw new InvalidOperationException("The exchange WebSocket permits one reader.");
        try
        {
            while (await ReadFrameAsync(_socket, _maxMessageBytes, _key, ct).ConfigureAwait(false) is { } frame)
            {
                if (frame.Type == WebSocketMessageType.Text)
                    yield return ReadJson(frame.Bytes);
                else if (_encoding == LseExchangeEncoding.Dbn)
                    yield return ReadDbn(frame.Bytes, _level);
                else
                    throw Protocol("Unexpected binary frame in a JSON subscription.");
            }
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    private static LseExchangeJsonMessage ReadJson(byte[] bytes)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(bytes.AsSpan());
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String && type.GetString() is { Length: > 0 } name)
                return new(name, json);
        }
        catch (JsonException)
        {
            throw Protocol("The exchange sent malformed JSON.");
        }
        throw Protocol("The exchange sent a JSON message without a type.");
    }

    internal static LseExchangeDbnBatch ReadDbn(byte[] bytes, LseBookLevel level)
    {
        const int header = 24;
        var span = bytes.AsSpan();
        if (span.Length < header || !span.StartsWith("L3DB"u8) || span[4] != 1 || span[5] is not (1 or 2))
            throw Protocol("Unexpected L3DB binary envelope.");
        var recordSize = level switch
        {
            LseBookLevel.BestBidOffer => 80, LseBookLevel.PriceLevels => 368,
            LseBookLevel.Orders => 56, _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
        if ((long)count * recordSize != span.Length - header)
            throw Protocol("The L3DB record count does not match the payload length.");
        return new LseExchangeDbnBatch(span[5] == 1, BinaryPrimitives.ReadUInt64LittleEndian(span[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[16..]), (int)count, recordSize, bytes.AsMemory(header));
    }

    private static async Task<(WebSocketMessageType Type, byte[] Bytes)?> ReadFrameAsync(
        ClientWebSocket socket, int maximum, string key, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var payload = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var status = socket.CloseStatus;
                    var reason = LseException.Redact(socket.CloseStatusDescription ?? "", key, 123);
                    using var close = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    close.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, close.Token).ConfigureAwait(false);
                    }
                    catch (WebSocketException) when (!ct.IsCancellationRequested) { }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && close.IsCancellationRequested) { }
                    if (status == WebSocketCloseStatus.NormalClosure)
                        return null;
                    throw new LseException(0, "stream-closed", $"Exchange close {(int?)status}: {reason}")
                    { WebSocketCloseStatus = status };
                }
                if (payload.Length + result.Count > maximum)
                    throw new LseException(0, "stream-message-too-large", "The exchange message exceeds the configured limit.");
                payload.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            return (result.MessageType, payload.ToArray());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private static LseException Protocol(string message) => new(0, "stream-protocol", message);

    internal static void ValidateLevel(LseBookLevel level)
    {
        if (level is < LseBookLevel.BestBidOffer or > LseBookLevel.Orders)
            throw new ArgumentOutOfRangeException(nameof(level));
    }

    /// <summary>Releases the WebSocket and stops reading the exchange feed.</summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

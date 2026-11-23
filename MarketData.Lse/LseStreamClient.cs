using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LSE.NET;

/// <summary>One authenticated connection to the standard LSE live price protocol.</summary>
/// <remarks>Start one reader promptly and keep it draining. Commands can run concurrently;
/// responses (including refusals) arrive in the reader. A terminated connection is not reusable.</remarks>
public sealed class LseStreamClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly LseStreamOptions _options;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _lifetime;
    private Task _heartbeat = Task.CompletedTask;
    private int _reader;
    private int _disposed;

    private LseStreamClient(LseStreamOptions options)
    {
        _options = options;
        _lifetime = _stop.Token;
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>The authentication reply, including the plan and initial symbol catalogue.</summary>
    public LseStreamMessage Authentication { get; private set; } = null!;

    /// <summary>Opens a connection, waits for welcome, and authenticates before returning.</summary>
    public static async Task<LseStreamClient> ConnectAsync(LseStreamOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        LseOptions.ValidateKey(options.ApiKey);
        if (options.Endpoint is null || !LseOptions.IsSecureEndpoint(options.Endpoint, "wss", "ws")
            || options.Endpoint.Query.Length > 0)
            throw new ArgumentException("Expected a WSS endpoint without a query (WS loopback is allowed).", nameof(options));
        if (options.ConnectTimeout <= TimeSpan.Zero || options.ConnectTimeout > TimeSpan.FromHours(1)
            || options.PingInterval <= TimeSpan.Zero || options.PingInterval > TimeSpan.FromMinutes(1)
            || options.MaxMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid stream timeout, ping interval or size limit.");

        var client = new LseStreamClient(options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ConnectTimeout);
        try
        {
            await client._socket.ConnectAsync(options.Endpoint, timeout.Token).ConfigureAwait(false);
            client.CheckHandshake(await client.ReceiveAsync(timeout.Token).ConfigureAwait(false), "welcome");
            await client.SendAsync(new { action = "auth", api_key = options.ApiKey }, timeout.Token).ConfigureAwait(false);
            var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            client.CheckHandshake(result, "authenticated");
            client.Authentication = result!;
            client._heartbeat = client.PingLoopAsync();
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            if (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                throw new LseException(0, "stream-timeout", "The WebSocket handshake timed out.");
            throw;
        }
    }

    /// <summary>Sends a subscription command for one socket-specific symbol.</summary>
    public Task SubscribeAsync(string symbol, CancellationToken ct = default) =>
        SendAsync(new { action = "subscribe", symbol = Required(symbol) }, ct);

    /// <summary>Sends a command to remove one symbol subscription.</summary>
    public Task UnsubscribeAsync(string symbol, CancellationToken ct = default) =>
        SendAsync(new { action = "unsubscribe", symbol = Required(symbol) }, ct);

    /// <summary>Sends a command to subscribe to all listed contracts of an underlying.</summary>
    public Task SubscribeOptionsAsync(string underlying, CancellationToken ct = default) =>
        SendAsync(new { action = "subscribe_options", underlying = Required(underlying) }, ct);

    /// <summary>Sends a command to unsubscribe from an underlying's option contracts.</summary>
    public Task UnsubscribeOptionsAsync(string underlying, CancellationToken ct = default) =>
        SendAsync(new { action = "unsubscribe_options", underlying = Required(underlying) }, ct);

    /// <summary>Requests the symbol catalogue (optionally category "options" or "all").</summary>
    public Task ListSymbolsAsync(string? category = null, CancellationToken ct = default) =>
        category is null ? SendAsync(new { action = "list_symbols" }, ct)
            : SendAsync(new { action = "list_symbols", category = Required(category) }, ct);

    /// <summary>Sends an application ping. Its pong is delivered to the reader.</summary>
    public Task PingAsync(CancellationToken ct = default) => SendAsync(new { action = "ping" }, ct);

    /// <summary>Reads messages in wire order. Enumerate once; leaving the loop closes the connection.</summary>
    public async IAsyncEnumerable<LseStreamMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _reader, 1) != 0)
            throw new InvalidOperationException("The WebSocket allows one read loop per connection.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime);
        try
        {
            while (await ReceiveAsync(linked.Token).ConfigureAwait(false) is { } message)
                yield return message;
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    private async Task<LseStreamMessage?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var payload = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (_socket.CloseStatus == WebSocketCloseStatus.NormalClosure)
                    {
                        await AcknowledgeCloseAsync(ct).ConfigureAwait(false);
                        return null;
                    }
                    var status = _socket.CloseStatus;
                    await AcknowledgeCloseAsync(ct).ConfigureAwait(false);
                    throw new LseException(0, "stream-closed", $"The WebSocket closed with status {(int?)status}.");
                }
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new LseException(0, "stream-protocol", "Expected a text WebSocket message.");
                if (payload.Length + result.Count > _options.MaxMessageBytes)
                    throw new LseException(0, "stream-message-too-large", "The WebSocket message exceeds the configured limit.");
                payload.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = JsonSerializer.Deserialize<JsonElement>(payload.GetBuffer().AsSpan(0, (int)payload.Length));
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(type.GetString()))
                throw new LseException(0, "stream-protocol", "The WebSocket message has no type.");
            return new LseStreamMessage(type.GetString()!, json);
        }
        catch (JsonException)
        {
            throw new LseException(0, "stream-protocol", "The WebSocket sent invalid JSON.");
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is WebSocketException or OperationCanceledException)
        {
            throw new LseException(0, "stream-disconnected", "The WebSocket transport was interrupted.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private async Task AcknowledgeCloseAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await _sendGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    private async Task SendAsync<T>(T command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime);
        await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(command);
            try
            {
                await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, linked.Token).ConfigureAwait(false);
            }
            finally { Array.Clear(bytes); }
        }
        finally { _sendGate.Release(); }
    }

    private async Task PingLoopAsync()
    {
        using var timer = new PeriodicTimer(_options.PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime).ConfigureAwait(false))
            {
                if (_socket.State != WebSocketState.Open)
                    return;
                await PingAsync(_lifetime).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        catch (Exception e) when (e is WebSocketException or InvalidOperationException or OperationCanceledException)
        {
            // Unblock the reader so the consumer owns reconnect policy.
            if (_socket.State == WebSocketState.Open)
                _socket.Abort();
        }
    }

    private void CheckHandshake(LseStreamMessage? message, string expected)
    {
        if (message?.Type == expected)
            return;
        if (message?.Type == "error")
            throw LseException.Parse(0, message.Payload.GetRawText(), secret: _options.ApiKey);
        throw new LseException(0, "stream-protocol", $"Expected a WebSocket {expected} message.");
    }

    private static string Required(string value, [CallerArgumentExpression(nameof(value))] string? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, param);
        return value;
    }

    /// <summary>Stops heartbeats and closes the transport without waiting for a peer reply.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _socket.Dispose();
        await _heartbeat.ConfigureAwait(false);
        _stop.Dispose();
    }
}

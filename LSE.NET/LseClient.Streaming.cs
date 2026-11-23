using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LSE.NET;

public sealed partial class LseClient
{
    private StreamSession? _session;
    private int _disposed;
    internal Uri LondonStreamEndpoint { get; init; } = LseStreamEndpoints.London;
    internal Uri DataStreamEndpoint { get; init; } = LseStreamEndpoints.Data;

    /// <summary>Raised for feed acknowledgements, notices, and command errors on the active stream.</summary>
    public event Action<LseStreamMessage>? StreamMessage;

    /// <summary>Raised for each tick consumed by StreamAsync or ConnectAsync.</summary>
    public event Action<LseLiveTick>? Tick;

    /// <summary>Streams live ticks, routing catalogue-listed crypto to London unless an endpoint was specified.</summary>
    public async IAsyncEnumerable<LseLiveTick> StreamAsync(IEnumerable<string> symbols,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var initial = ValidateSymbols(symbols);
        var session = new StreamSession(this, ct);
        if (Interlocked.CompareExchange(ref _session, session, null) is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("This client already has an active stream.");
        }
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!await session.StartOrStopAsync(initial).ConfigureAwait(false))
                yield break;
            await using var updates = session.ReadAsync().GetAsyncEnumerator();
            while (await session.MoveNextAsync(updates).ConfigureAwait(false))
            {
                var message = updates.Current;
                if (message.GetTick() is { } tick)
                {
                    Tick?.Invoke(tick);
                    yield return tick;
                }
                else
                    StreamMessage?.Invoke(message);
            }
        }
        finally
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            finally { Interlocked.CompareExchange(ref _session, null, session); }
        }
    }

    /// <summary>Runs the live stream until it closes; consume updates through the Tick event.</summary>
    public async Task ConnectAsync(IEnumerable<string>? symbols = null, CancellationToken ct = default)
    {
        await foreach (var _ in StreamAsync(symbols ?? [], ct).ConfigureAwait(false)) { }
    }

    /// <summary>Subscribes to symbols on the active connection.</summary>
    public Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default) =>
        ActiveStream().SendAsync(ValidateSymbols(symbols), subscribe: true, options: false, ct);

    /// <summary>Removes symbol subscriptions on the active connection.</summary>
    public Task UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default) =>
        ActiveStream().SendAsync(ValidateSymbols(symbols), subscribe: false, options: false, ct);

    /// <summary>Subscribes to all listed contracts of each underlying.</summary>
    public Task SubscribeOptionsAsync(IEnumerable<string> underlyings, CancellationToken ct = default) =>
        ActiveStream().SendAsync(ValidateSymbols(underlyings), subscribe: true, options: true, ct);

    /// <summary>Removes option-chain subscriptions on the active connection.</summary>
    public Task UnsubscribeOptionsAsync(IEnumerable<string> underlyings, CancellationToken ct = default) =>
        ActiveStream().SendAsync(ValidateSymbols(underlyings), subscribe: false, options: true, ct);

    /// <summary>Signals the active stream to stop. Await the stream task to observe its completion.</summary>
    public void Disconnect() => Volatile.Read(ref _session)?.Stop();

    /// <summary>Stops the live session and disposes the owned HTTP client.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Disconnect();
        Http.Dispose();
    }

    private StreamSession ActiveStream() => Volatile.Read(ref _session)
        ?? throw new InvalidOperationException("Start a live stream before changing subscriptions.");

    private static string[] ValidateSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        return symbols.Select(symbol =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            return symbol.Trim();
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class StreamSession
    {
        private readonly LseClient _owner;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenSource _stop;
        private readonly CancellationToken _lifetime;
        private readonly Channel<LseStreamMessage> _messages = Channel.CreateBounded<LseStreamMessage>(512);
        private readonly SemaphoreSlim _commands = new(1, 1);
        private readonly HashSet<string> _crypto = new(StringComparer.OrdinalIgnoreCase);
        private LseStreamClient? _main;
        private LseStreamClient? _london;
        private Task? _mainPump;
        private Task? _londonPump;
        private bool _ready;
        private int _stopped;

        public StreamSession(LseClient owner, CancellationToken ct)
        {
            _owner = owner;
            _callerToken = ct;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _lifetime = _stop.Token;
        }

        public bool IsStopped => Volatile.Read(ref _stopped) != 0;

        public async Task<bool> StartOrStopAsync(string[] symbols)
        {
            try
            {
                await StartAsync(symbols).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (IsStopped && !_callerToken.IsCancellationRequested) { return false; }
        }

        public async Task<bool> MoveNextAsync(IAsyncEnumerator<LseStreamMessage> updates)
        {
            try { return await updates.MoveNextAsync().ConfigureAwait(false) && !IsStopped; }
            catch (OperationCanceledException) when (IsStopped && !_callerToken.IsCancellationRequested) { return false; }
        }

        private async Task StartAsync(string[] symbols)
        {
            await _commands.WaitAsync(_lifetime).ConfigureAwait(false);
            try
            {
                _main = await OpenAsync(_owner._streamEndpoint ?? _owner.DataStreamEndpoint, _lifetime).ConfigureAwait(false);
                if (_owner._streamEndpoint is null)
                    LearnCrypto(_main.Authentication.Payload);
                _mainPump = PumpAsync(_main, primary: true);
                foreach (var symbol in symbols.OrderBy(symbol => _crypto.Contains(symbol)))
                {
                    try { await SendOneAsync(symbol, subscribe: true, options: false, _lifetime).ConfigureAwait(false); }
                    catch (Exception e) when (_crypto.Contains(symbol) && e is LseException or System.Net.WebSockets.WebSocketException)
                    {
                        ReportLondonFailure(e);
                        break;
                    }
                }
                _ready = true;
            }
            finally { _commands.Release(); }
        }

        public IAsyncEnumerable<LseStreamMessage> ReadAsync() => _messages.Reader.ReadAllAsync(_lifetime);

        public async Task SendAsync(string[] symbols, bool subscribe, bool options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsStopped)
                throw new InvalidOperationException("The live stream has stopped.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime);
            await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (!_ready)
                    throw new InvalidOperationException("The live stream has not authenticated.");
                foreach (var symbol in symbols)
                    await SendOneAsync(symbol, subscribe, options, linked.Token).ConfigureAwait(false);
            }
            finally { _commands.Release(); }
        }

        private async Task SendOneAsync(string symbol, bool subscribe, bool options, CancellationToken token)
        {
            var socket = _main!;
            if (!options && _crypto.Contains(symbol))
            {
                if ((_london is null || _londonPump?.IsCompleted is true) && subscribe)
                {
                    _london = await OpenAsync(_owner.LondonStreamEndpoint, token).ConfigureAwait(false);
                    _londonPump = PumpAsync(_london, primary: false);
                }
                if (_london is null || _londonPump?.IsCompleted is true)
                    return;
                socket = _london;
            }
            await (options
                ? (subscribe ? socket.SubscribeOptionsAsync(symbol, token) : socket.UnsubscribeOptionsAsync(symbol, token))
                : (subscribe ? socket.SubscribeAsync(symbol, token) : socket.UnsubscribeAsync(symbol, token)))
                .ConfigureAwait(false);
        }

        private Task<LseStreamClient> OpenAsync(Uri endpoint, CancellationToken token) =>
            LseStreamClient.ConnectAsync(new() { ApiKey = _owner._key, Endpoint = endpoint }, token);

        private async Task PumpAsync(LseStreamClient socket, bool primary)
        {
            try
            {
                await _messages.Writer.WriteAsync(socket.Authentication, _lifetime).ConfigureAwait(false);
                await foreach (var message in socket.ReadMessagesAsync(_lifetime).ConfigureAwait(false))
                    await _messages.Writer.WriteAsync(message, _lifetime).ConfigureAwait(false);
                if (primary)
                    _messages.Writer.TryComplete();
                else if (!_stop.IsCancellationRequested)
                    ReportLondonFailure(new LseException(0, "stream-disconnected", "The London stream closed."));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (ChannelClosedException) { }
            catch (Exception e) when (!primary && e is LseException or System.Net.WebSockets.WebSocketException)
            {
                ReportLondonFailure(e);
            }
            catch (Exception e) { _messages.Writer.TryComplete(e); }
        }

        private void ReportLondonFailure(Exception error)
        {
            if (!_messages.Writer.TryWrite(new("error", System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    type = "error", endpoint = _owner.LondonStreamEndpoint.AbsoluteUri,
                    code = error is LseException lse ? lse.Code : "stream-disconnected", message = error.Message
                }))))
                _messages.Writer.TryComplete(error);
        }

        private void LearnCrypto(System.Text.Json.JsonElement auth)
        {
            if (!auth.TryGetProperty("symbols", out var catalogue)
                || catalogue.ValueKind != System.Text.Json.JsonValueKind.Array)
                return;
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in catalogue.EnumerateArray())
            {
                if (Text(row, "symbol") is not { Length: > 0 } symbol)
                    continue;
                listed.Add(symbol);
                if (string.Equals(Text(row, "category"), "crypto", StringComparison.OrdinalIgnoreCase))
                    _crypto.Add(symbol);
            }
            foreach (var alias in _crypto.Where(symbol => symbol.Contains('/')).Select(symbol => symbol.Replace("/", "")).ToArray())
                if (!listed.Contains(alias))
                    _crypto.Add(alias);
        }

        public void Stop()
        {
            lock (_stop)
                if (Interlocked.Exchange(ref _stopped, 1) == 0)
                    _stop.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            await _commands.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_mainPump is not null) await _mainPump.ConfigureAwait(false);
                if (_londonPump is not null) await _londonPump.ConfigureAwait(false);
                if (_main is not null) await _main.DisposeAsync().ConfigureAwait(false);
                if (_london is not null) await _london.DisposeAsync().ConfigureAwait(false);
            }
            finally { _commands.Release(); }
            lock (_stop) _stop.Dispose();
        }
    }
}

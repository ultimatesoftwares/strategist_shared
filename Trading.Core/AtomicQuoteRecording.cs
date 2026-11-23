using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace Trading.Core;

/// <summary>Session metadata; symbol IDs are local to this recording.</summary>
public sealed record AtomicQuoteRecordingHeader(
    int Version, Guid SessionId, Guid VenueId, long PriceScale,
    DateTimeOffset StartedUtc, IReadOnlyDictionary<uint, Guid> Instruments,
    Guid? ClockDomainId = null);

public sealed record AtomicQuoteRecordingData(
    AtomicQuoteRecordingHeader Header, IReadOnlyList<TickReplayEvent> Events);

/// <summary>
/// Bounded, asynchronous quote recorder. An incomplete file, dropped quote,
/// sequence regression or writer error must never be treated as replayable.
/// The caller controls session lifetime and must report upstream interruptions.
/// </summary>
public sealed class AtomicQuoteRecorder
{
    private sealed record Line(
        string Kind, AtomicQuoteRecordingHeader? Header = null, long Sequence = 0,
        long ReceivedNanos = 0, QuoteTick? Quote = null, long Count = 0, bool Complete = false,
        string? Checksum = null);

    private readonly Channel<Line> _queue;
    private readonly Task _writer;
    private readonly AtomicQuoteRecordingHeader _header;
    private int _accepting = 1;
    private int _inFlight;
    private int _invalid;
    private long _sequence = -1;
    private Task? _completion;
    private readonly object _completionGate = new();

    public string FilePath { get; }
    public bool Invalid => Volatile.Read(ref _invalid) != 0;
    public Task WriterCompletion => _writer;

    public AtomicQuoteRecorder(string filePath, AtomicQuoteRecordingHeader header, int capacity = 8192)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(header);
        if (header.Version != 1 || header.SessionId == Guid.Empty || header.VenueId == Guid.Empty
            || header.PriceScale <= 0 || header.Instruments is null || header.Instruments.Count == 0
            || header.Instruments.Values.Any(id => id == Guid.Empty)
            || header.Instruments.Values.Distinct().Count() != header.Instruments.Count)
            throw new ArgumentException("Invalid recording metadata.", nameof(header));
        if (capacity is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        FilePath = Path.GetFullPath(filePath);
        _header = header with { Instruments = new Dictionary<uint, Guid>(header.Instruments) };
        _queue = Channel.CreateBounded<Line>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait,
        });
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>Never waits for disk or queue space; false marks the recording incomplete.</summary>
    public bool TryRecord(long receivedNanos, QuoteTick quote)
    {
        if (Volatile.Read(ref _accepting) == 0)
            return false;
        Interlocked.Increment(ref _inFlight);
        try
        {
            if (Volatile.Read(ref _accepting) == 0)
                return false;
            if (receivedNanos < 0 || !_header.Instruments.ContainsKey(quote.SymbolId))
            {
                Invalidate();
                return false;
            }
            var sequence = Interlocked.Increment(ref _sequence);
            if (sequence < 0 || !_queue.Writer.TryWrite(new Line("quote", Sequence: sequence,
                    ReceivedNanos: receivedNanos, Quote: quote)))
            {
                Invalidate();
                return false;
            }
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public void Invalidate() => Volatile.Write(ref _invalid, 1);

    /// <summary>Stops input, drains accepted records and writes the completion footer.</summary>
    public Task CompleteAsync()
    {
        lock (_completionGate)
            return _completion ??= CompleteCoreAsync();
    }

    private async Task CompleteCoreAsync()
    {
        Volatile.Write(ref _accepting, 0);
        while (Volatile.Read(ref _inFlight) != 0)
            await Task.Delay(1);
        _queue.Writer.TryComplete();
        await _writer;
    }

    private async Task WriteAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            await using var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                65536, FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = JsonSerializer.Serialize(new Line("header", Header: _header));
            AddHash(hash, header);
            await writer.WriteLineAsync(header);
            long count = 0;
            long lastTime = -1;
            await foreach (var line in _queue.Reader.ReadAllAsync())
            {
                if (line.Sequence != count || line.ReceivedNanos < lastTime)
                    Invalidate();
                count++;
                lastTime = line.ReceivedNanos;
                var text = JsonSerializer.Serialize(line);
                AddHash(hash, text);
                await writer.WriteLineAsync(text);
                if (count % 256 == 0)
                    await writer.FlushAsync();
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(new Line("footer", Count: count, Complete: !Invalid,
                Checksum: Convert.ToHexString(hash.GetHashAndReset()))));
            await writer.FlushAsync();
        }
        catch
        {
            Invalidate();
            _queue.Writer.TryComplete();
            throw;
        }
    }

    /// <summary>Reads only a complete, ordered recording and returns atomic quote replay events.</summary>
    public static AtomicQuoteRecordingData Read(string path)
    {
        using var reader = File.OpenText(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static Line Parse(string text)
        {
            try { return JsonSerializer.Deserialize<Line>(text) ?? throw new InvalidDataException("Null recording row."); }
            catch (JsonException ex) { throw new InvalidDataException("Malformed recording row.", ex); }
        }
        var first = reader.ReadLine() ?? throw new InvalidDataException("Missing recording header.");
        AddHash(hash, first);
        var header = Parse(first);
        if (header.Kind != "header" || header.Header is not { } metadata || metadata.Version != 1
            || metadata.SessionId == Guid.Empty || metadata.VenueId == Guid.Empty || metadata.PriceScale <= 0
            || metadata.Instruments is null || metadata.Instruments.Count == 0
            || metadata.Instruments.Values.Any(id => id == Guid.Empty)
            || metadata.Instruments.Values.Distinct().Count() != metadata.Instruments.Count)
            throw new InvalidDataException("Invalid recording header.");
        var events = new List<TickReplayEvent>();
        long lastTime = -1;
        while (reader.ReadLine() is { } text)
        {
            var line = Parse(text);
            if (line.Kind == "footer")
            {
                if (!line.Complete || line.Count != events.Count || reader.ReadLine() is not null
                    || line.Checksum != Convert.ToHexString(hash.GetHashAndReset()))
                    throw new InvalidDataException("Recording is incomplete or has trailing data.");
                return new AtomicQuoteRecordingData(metadata, events);
            }
            if (line.Kind != "quote" || line.Quote is not { } quote
                || line.Sequence != events.Count || line.ReceivedNanos < 0 || line.ReceivedNanos < lastTime
                || !metadata.Instruments.ContainsKey(quote.SymbolId))
                throw new InvalidDataException("Invalid or out-of-order recording row.");
            events.Add(new TickReplayEvent.Quote((ulong)line.ReceivedNanos, quote));
            AddHash(hash, text);
            lastTime = line.ReceivedNanos;
        }
        throw new InvalidDataException("Recording has no completion footer.");
    }

    private static void AddHash(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData("\n"u8);
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Engine.Control;

/// <summary>Latest engine-state entry per subject with fan-out to gRPC
/// subscribers — the in-process successor of the engine-state JetStream
/// stream (MaxMsgsPerSubject=1 semantics).</summary>
public sealed class EngineStateHub
{
    public sealed record Entry(string Subject, string Json, long UnixMs);

    private readonly ConcurrentDictionary<string, Entry> _snapshot = new();
    private readonly ConcurrentDictionary<Guid, Channel<Entry>> _subscribers = new();

    public void Publish(string subject, string json)
    {
        var entry = new Entry(subject, json, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _snapshot[subject] = entry;
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(entry);
    }

    /// <summary>Drops a subject (stopped instance): removed from the snapshot
    /// and fanned out as a tombstone (empty Json) so downstream stores delete
    /// it instead of aging it out.</summary>
    public void Remove(string subject)
    {
        _snapshot.TryRemove(subject, out _);
        var tombstone = new Entry(subject, "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(tombstone);
    }

    /// <summary>Latest state for every subject seen so far.</summary>
    public IReadOnlyCollection<Entry> Snapshot() =>
        _snapshot.Values.OrderBy(e => e.Subject).ToList();

    public (Guid Id, ChannelReader<Entry> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<Entry>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }
}

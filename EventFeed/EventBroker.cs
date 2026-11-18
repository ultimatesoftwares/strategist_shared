using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace EventFeed;

/// <summary>
/// One serialized feed event: the subject it was published on, the payload
/// JSON, and the server receive time (unix ms). Proto-agnostic — each gRPC
/// feed service maps this onto its own wire message (the bot's
/// serverevents.EngineStateUpdate, the backtest's
/// backtestevents.BacktestEventUpdate).
/// </summary>
public sealed record FeedEvent(string Subject, string Json, long ReceivedAtMs);

/// <summary>
/// The in-process hub behind the gRPC push feeds: producers publish
/// subject-addressed payloads, subscribers get the latest state per matching
/// subject replayed, then every live update.
///
/// Designed for market-data hot paths on the publish side:
///   - <see cref="Publish"/> never blocks — each subscriber has a bounded
///     drop-oldest channel, so a slow consumer only loses its own history.
///   - payloads are serialized to JSON lazily and at most once per publish,
///     and not at all when nobody is listening (the common idle case).
/// </summary>
public sealed class EventBroker
{
    /// <summary>Camel-case + enum names: the shape REST consumers already see.</summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CachedEvent(object Payload, long TsMs);

    private sealed class Subscriber
    {
        public required string Prefix { get; init; }
        public required Channel<FeedEvent> Channel { get; init; }
    }

    // Latest payload per subject, kept unserialized: replay only pays the JSON
    // cost for the subjects a new subscriber actually matches.
    private readonly ConcurrentDictionary<string, CachedEvent> _latest = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Subscriber> _subscribers = new();
    private long _nextSubscriberId;

    /// <summary>Latest-event channel depth per subscriber; overflow drops the oldest.</summary>
    private const int SubscriberCapacity = 10_000;

    public int SubscriberCount => _subscribers.Count;

    /// <summary>
    /// Records <paramref name="payload"/> as the latest state of
    /// <paramref name="subject"/> and fans it out to matching subscribers.
    /// Never blocks and never throws for serialization-safe payloads.
    /// </summary>
    public void Publish(string subject, object payload)
    {
        var tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _latest[subject] = new CachedEvent(payload, tsMs);

        if (_subscribers.IsEmpty)
            return;

        // Serialize once, share the update across subscribers.
        FeedEvent? update = null;
        foreach (var subscriber in _subscribers.Values)
        {
            if (!subject.StartsWith(subscriber.Prefix, StringComparison.Ordinal))
                continue;
            update ??= new FeedEvent(subject, JsonSerializer.Serialize(payload, JsonOptions), tsMs);
            // Bounded drop-oldest: TryWrite only fails on completion.
            subscriber.Channel.Writer.TryWrite(update);
        }
    }

    /// <summary>
    /// Registers a subscriber for subjects starting with
    /// <paramref name="prefix"/> (empty = all). Returns the replay snapshot
    /// (latest state per matching subject at subscribe time), the live reader,
    /// and a handle that must be disposed to unregister. The subscriber is
    /// registered before the snapshot is taken, so an update racing the
    /// subscribe may appear in both — last write wins for state consumers.
    /// </summary>
    public (IDisposable Registration, IReadOnlyList<FeedEvent> Snapshot,
        ChannelReader<FeedEvent> Live) Subscribe(string prefix)
    {
        prefix ??= "";
        var channel = Channel.CreateBounded<FeedEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var id = Interlocked.Increment(ref _nextSubscriberId);
        _subscribers[id] = new Subscriber { Prefix = prefix, Channel = channel };

        var snapshot = new List<FeedEvent>();
        foreach (var (subject, cached) in _latest)
        {
            if (!subject.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            snapshot.Add(new FeedEvent(
                subject, JsonSerializer.Serialize(cached.Payload, JsonOptions), cached.TsMs));
        }

        return (new Registration(this, id), snapshot, channel.Reader);
    }

    private void Unsubscribe(long id)
    {
        if (_subscribers.TryRemove(id, out var subscriber))
            subscriber.Channel.Writer.TryComplete();
    }

    private sealed class Registration(EventBroker broker, long id) : IDisposable
    {
        public void Dispose() => broker.Unsubscribe(id);
    }
}

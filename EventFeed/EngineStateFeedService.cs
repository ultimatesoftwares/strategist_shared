using Grpc.Core;

using Strategist.ServerEvents;

namespace EventFeed;

/// <summary>
/// The bot's gRPC surface of the <see cref="EventBroker"/>: implements
/// <c>serverevents.EngineStateFeed</c> (shared/Protos/bot/v1/bot_events.proto).
/// Each subscriber gets the latest state per matching subject replayed, then
/// live updates until it disconnects. Slow consumers see the newest events
/// (older ones are dropped per-subscriber), never a stall.
/// </summary>
public sealed class EngineStateFeedService : EngineStateFeed.EngineStateFeedBase
{
    private readonly EventBroker _broker;

    public EngineStateFeedService(EventBroker broker) => _broker = broker;

    public override async Task Subscribe(
        EngineStateSubscribeRequest request,
        IServerStreamWriter<EngineStateUpdate> responseStream,
        ServerCallContext context)
    {
        var (registration, snapshot, live) = _broker.Subscribe(request.Prefix ?? "");
        using (registration)
        {
            try
            {
                foreach (var update in snapshot)
                    await responseStream.WriteAsync(ToWire(update), context.CancellationToken);
                await foreach (var update in live.ReadAllAsync(context.CancellationToken))
                    await responseStream.WriteAsync(ToWire(update), context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client went away — normal stream teardown.
            }
        }
    }

    private static EngineStateUpdate ToWire(FeedEvent update) => new()
    {
        Subject = update.Subject,
        Json = update.Json,
        ReceivedAtMs = update.ReceivedAtMs,
    };
}

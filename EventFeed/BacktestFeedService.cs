using Grpc.Core;

using Strategist.BacktestEvents;

namespace EventFeed;

/// <summary>
/// The backtest API's gRPC surface of the <see cref="EventBroker"/>:
/// implements <c>backtestevents.BacktestFeed</c>
/// (shared/Protos/backtest/v1/backtest_events.proto). Same replay-then-live
/// semantics as the bot's FIX feed plane (U30/U31) — a separate wire
/// contract so the backtest feed can evolve independently of the bot's.
/// </summary>
public sealed class BacktestFeedService : BacktestFeed.BacktestFeedBase
{
    private readonly EventBroker _broker;

    public BacktestFeedService(EventBroker broker) => _broker = broker;

    public override async Task Subscribe(
        BacktestSubscribeRequest request,
        IServerStreamWriter<BacktestEventUpdate> responseStream,
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

    private static BacktestEventUpdate ToWire(FeedEvent update) => new()
    {
        Subject = update.Subject,
        Json = update.Json,
        ReceivedAtMs = update.ReceivedAtMs,
    };
}

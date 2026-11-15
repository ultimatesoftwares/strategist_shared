namespace Strategist.Contracts.Bus;

/// <summary>
/// The engine's state plane: latest-per-subject GUI state published on the bus
/// as <see cref="V1.EngineStateChanged"/> (subject + JSON payload; a null/empty
/// payload tombstones the subject). Payload shapes live in
/// <c>Strategist.Contracts.V1</c> and are frozen by the golden-payload
/// characterization tests.
/// </summary>
public static class EngineStateBus
{
    /// <summary>engine.state.quote.{venueId} — <see cref="V1.EngineQuotesState"/>.</summary>
    public static string QuoteSubject(string venueId) => $"engine.state.quote.{venueId}";
    public const string QuoteSubjectPrefix = "engine.state.quote.";

    /// <summary>engine.state.venue.{kind}.{venueId} — <see cref="V1.EngineVenueState"/>.</summary>
    public static string VenueSubject(string kind, string venueId)
        => $"engine.state.venue.{kind}.{venueId}";
    public const string VenueSubjectPrefix = "engine.state.venue.";

    /// <summary>engine.state.trading_mode — <see cref="V1.TradingModeState"/>.</summary>
    public const string TradingModeSubject = "engine.state.trading_mode";

    /// <summary>engine.state.orphans — <see cref="V1.OrphansState"/>.</summary>
    public const string OrphansSubject = "engine.state.orphans";

    /// <summary>engine.state.health — the engine's .NET health report
    /// (venue connectivity + quote staleness), mirrored to the server by
    /// the bus state publisher like every other engine.state.* subject.</summary>
    public const string EngineHealthSubject = "engine.state.health";

    /// <summary>health.strategy.{instanceId} — <see cref="V1.StrategyHealthState"/>.</summary>
    public static string StrategyHealthSubject(string instanceId) => $"health.strategy.{instanceId}";
    public const string StrategyHealthSubjectPrefix = "health.strategy.";
}

/// <summary>
/// The bus contract between the server (master) and the Backtest slave.
/// The server sends job requests to the durable <see cref="JobsQueue"/>
/// (<see cref="V1.PackageBacktestJobMessage"/>)
/// and publishes cancellations (<see cref="V1.BacktestCancelMessage"/>); the
/// slave publishes job progress and terminal states
/// (<see cref="V1.PackageBacktestStateMessage"/>) plus a periodic
/// <see cref="V1.WorkerHeartbeat"/> carrying the ids it is currently running,
/// which the server's watchdog consumes to update rows and detect lost jobs.
/// Market-data provisioning left this queue for <see cref="FleetDataQueue"/>:
/// venue history lives on the fleet hub with the broker SDKs. Strategy
/// packages are built desktop-side (uv) and verified by the server on upload
/// — no build jobs ride this queue anymore.
/// </summary>
public static class BacktestQueue
{
    /// <summary>Durable work queue the slave consumes.</summary>
    public const string JobsQueue = "backtest.jobs";

    /// <summary>Heartbeat cadence; the server treats a running row whose id
    /// is absent from fresh heartbeats as lost on the worker.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The bus contract between the server (master) and the FLEET HUB's
/// market-data provisioning worker. The server sends
/// <see cref="V1.ProvisionJobMessage"/> to the durable <see cref="JobsQueue"/>
/// and publishes cancellations (<see cref="V1.BacktestCancelMessage"/>, shared
/// with the backtest plane); the hub fetches the range from the venue
/// (chunked, fault-tolerant, QuestDB dedup upserts) and publishes
/// <see cref="V1.ProvisionStateMessage"/> progress in the exact JSON shape the
/// GUI's provision-jobs list renders. No heartbeat: provision rows live in the
/// server's in-memory mirror, never in watchdog-swept tables.
/// </summary>
public static class FleetDataQueue
{
    /// <summary>Durable work queue the fleet hub consumes.</summary>
    public const string JobsQueue = "fleet.data.jobs";
}

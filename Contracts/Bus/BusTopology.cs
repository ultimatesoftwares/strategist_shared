namespace Strategist.Contracts.Bus;

/// <summary>
/// The engine's state plane: latest-per-subject GUI state, carried as
/// <see cref="V1.EngineStateChanged"/> frames (subject + JSON payload; a
/// null/empty payload tombstones the subject) over the engine's push feed
/// (the FIX feed plane, shared/FleetBridge/spec/FIX44-FLEET.xml U30/U31), fanned out to
/// desktop clients that subscribe with a subject prefix.
/// Payload shapes live in <c>Strategist.Contracts.V1</c> and are frozen by
/// the golden-payload characterization tests. (The former MassTransit/
/// RabbitMQ transport was retired: these subjects are the surviving,
/// transport-neutral vocabulary.)
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
    /// the state stream like every other engine.state.* subject.</summary>
    public const string EngineHealthSubject = "engine.state.health";

    /// <summary>health.strategy.{instanceId} — <see cref="V1.StrategyHealthState"/>.</summary>
    public static string StrategyHealthSubject(string instanceId) => $"health.strategy.{instanceId}";
    public const string StrategyHealthSubjectPrefix = "health.strategy.";
}

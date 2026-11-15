using System.Text.Json.Serialization;

namespace Strategist.Contracts.V1;

// Fleet control plane wire shapes: the server reaches each member's small
// control API with HTTP-shaped commands sent down the member's attached
// FleetControl stream (the member reverse-dials the server's gRPC listener;
// see shared/Protos/fleet_control.proto). Data planes (quotes, venue
// snapshots, engine state) ride gRPC streams too — nothing here carries
// market data. Property names and casing are the wire contract.

/// <summary>POST {controlUrl}/api/v1/engine/control — the engine control
/// relay (subjects: engine.state.snapshot, engine.cmd.instance.start/stop,
/// venues.changed, engine.cmd.trading_mode, engine.cmd.orphan.close).</summary>
public sealed record EngineControlRequest
{
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    /// <summary>JSON arguments ("" / "{}" = none).</summary>
    [JsonPropertyName("json")] public string Json { get; init; } = "";
}

/// <summary>Engine control reply: the subject handler's JSON payload.</summary>
public sealed record EngineControlReply
{
    [JsonPropertyName("json")] public required string Json { get; init; }
}

/// <summary>POST {controlUrl}/api/v1/engine/positions/close — close a broker
/// position through the engine's venue session.</summary>
public sealed record EngineCloseRequest
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("venueId")] public required string VenueId { get; init; }
    [JsonPropertyName("positionId")] public required string PositionId { get; init; }
    [JsonPropertyName("volume")] public long Volume { get; init; }
}

/// <summary>POST {controlUrl}/api/v1/engine/orders/cancel — cancel a pending
/// order through the engine's venue session.</summary>
public sealed record EngineCancelRequest
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("venueId")] public required string VenueId { get; init; }
    [JsonPropertyName("orderId")] public required string OrderId { get; init; }
}

/// <summary>Outcome of an engine position/order action.</summary>
public sealed record EngineActionReply
{
    [JsonPropertyName("ok")] public required bool Ok { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

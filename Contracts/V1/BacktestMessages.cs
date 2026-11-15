using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strategist.Contracts.V1;

// ─────────────────────────────────────────────────────────────────────────────
// The master ↔ slave job plane (gRPC since the RabbitMQ queues were
// retired; see Strategist.Contracts.Bus.BacktestQueue).
// Property names, casing and declaration order are the wire contract
// (reorg rule R1). Legacy snake_case names are deliberate.
// The legacy IR replay contracts (kind="backtest" jobs and their
// backtest.{id} state frames) were deleted in M9 with the Option B python/IR
// strategy path; package backtests below are the only replay lane.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>backtest.jobs (kind="provision"): one market-data fetch job.</summary>
public sealed record ProvisionJobMessage
{
    public const string JobKind = "provision";

    [JsonPropertyName("kind")] public string Kind { get; init; } = JobKind;
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("venueId")] public required string VenueId { get; init; }
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("timeframe")] public string Timeframe { get; init; } = "M1";
    /// <summary>UTC instant formatted "yyyy-MM-ddTHH:mm:ssZ" (legacy format).</summary>
    [JsonPropertyName("from")] public required string From { get; init; }
    /// <summary>UTC instant formatted "yyyy-MM-ddTHH:mm:ssZ" (legacy format).</summary>
    [JsonPropertyName("to")] public required string To { get; init; }
    /// <summary>Venue-side symbol id; OMITTED (not null) when unknown.</summary>
    [JsonPropertyName("symbolId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SymbolId { get; init; }
}

/// <summary>backtest.control (rk "cancel"): cancel one job by id.</summary>
public sealed record BacktestCancelMessage
{
    [JsonPropertyName("id")] public required string Id { get; init; }
}

/// <summary>provision.{id}: provisioning progress, in the exact JSON shape
/// the GUI's provision-jobs list renders.</summary>
public sealed record ProvisionStateMessage
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("timeframe")] public required string Timeframe { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    /// <summary>"running" | "completed" | "failed".</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }
    /// <summary>Failure detail; empty string (not null) when none.</summary>
    [JsonPropertyName("error")] public required string Error { get; init; }
    [JsonPropertyName("progress")] public required double Progress { get; init; }
    [JsonPropertyName("written")] public required long Written { get; init; }
    /// <summary>Unspecified-kind UTC DateTime (legacy: serialized without
    /// offset suffix).</summary>
    [JsonPropertyName("startedAt")] public required DateTime StartedAt { get; init; }
}

/// <summary>worker.heartbeat: the slave's liveness + held job ids; the
/// server fails Running rows absent from fresh heartbeats.</summary>
public sealed record WorkerHeartbeat
{
    [JsonPropertyName("worker")] public required string Worker { get; init; }
    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("ts")] public required long Ts { get; init; }
    [JsonPropertyName("running")] public required IReadOnlyList<string> Running { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Quant-platform package backtests (M4): the master sends kind:"package-backtest"
// jobs to the existing durable "backtest.jobs" queue; the Backtest slave fetches
// the immutable package from the Server, loads aligned bars from QuestDB per
// manifest leg, runs the deterministic Rust core backtester (CoreNative), and
// mirrors progress + the final capped report with PackageBacktestStateMessage
// frames. Property names,
// casing and declaration order are the wire contract (reorg rule R1).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>backtest.jobs (kind="package-backtest"): replay one immutable
/// strategy package over historical bars in the Rust core.</summary>
public sealed record PackageBacktestJobMessage
{
    public const string JobKind = "package-backtest";

    [JsonPropertyName("kind")] public string Kind { get; init; } = JobKind;
    [JsonPropertyName("id")] public required string Id { get; init; }
    /// <summary>Content hash of the package to replay (Server /api/packages).</summary>
    [JsonPropertyName("packageId")] public required string PackageId { get; init; }
    /// <summary>Bar timeframe; empty string means "use the manifest's
    /// timeframe".</summary>
    [JsonPropertyName("timeframe")] public string Timeframe { get; init; } = "";
    /// <summary>Unix seconds, inclusive start.</summary>
    [JsonPropertyName("from_ts")] public long FromTs { get; init; }
    /// <summary>Unix seconds, exclusive end.</summary>
    [JsonPropertyName("to_ts")] public long ToTs { get; init; }
    /// <summary>Hard cap on bars replayed; 0 means "no cap".</summary>
    [JsonPropertyName("max_bars")] public long MaxBars { get; init; }
    /// <summary>Unix seconds: bars at/after this instant are scored
    /// out-of-sample; 0 means "no in-sample/OOS split".</summary>
    [JsonPropertyName("oos_from_ts")] public long OosFromTs { get; init; }
    /// <summary>Fill model: "" (core default, next-bar open) |
    /// "next_open" | "current_close".</summary>
    [JsonPropertyName("fill")] public string Fill { get; init; } = "";
}

/// <summary>Package-backtest progress / terminal state, published by the
/// slave and mirrored by the Server onto the backtest_results row.</summary>
public sealed record PackageBacktestStateMessage
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    /// <summary>"running" | "completed" | "failed" | "cancelled".</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }
    /// <summary>Bars replayed so far (final count on terminal frames).</summary>
    [JsonPropertyName("bars")] public required long Bars { get; init; }
    /// <summary>Failure detail; empty string (not null) when none.</summary>
    [JsonPropertyName("error")] public required string Error { get; init; }
    /// <summary>The core's capped JSON report (summary + series + trades);
    /// OMITTED until status is "completed".</summary>
    [JsonPropertyName("report")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Report { get; init; }
    /// <summary>Unspecified-kind UTC DateTime (matches the provision-state
    /// legacy convention: serialized without offset suffix).</summary>
    [JsonPropertyName("startedAt")] public required DateTime StartedAt { get; init; }
}

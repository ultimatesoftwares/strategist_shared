using System.Text.Json.Serialization;

namespace Strategist.Contracts.V1;

// ─────────────────────────────────────────────────────────────────────────────
// engine.state.* — the engine's GUI state subjects. Property names, casing and
// DECLARATION ORDER are the wire contract (default System.Text.Json options;
// order = declaration order). Frozen by the golden-payload tests — do not add,
// remove or reorder properties without owner approval (reorg rule R1).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One symbol's latest quote inside <see cref="EngineQuotesState"/>.</summary>
public sealed record QuoteTick
{
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("bid")] public required double Bid { get; init; }
    [JsonPropertyName("ask")] public required double Ask { get; init; }
}

/// <summary>engine.state.quote.{venueId}: the venue's latest quotes.</summary>
public sealed record EngineQuotesState
{
    [JsonPropertyName("quotes")] public required IReadOnlyList<QuoteTick> Quotes { get; init; }
}

/// <summary>Broker account snapshot inside <see cref="EngineVenueState"/>.</summary>
public sealed record VenueAccountState
{
    [JsonPropertyName("balance")] public required double Balance { get; init; }
    [JsonPropertyName("equity")] public required double Equity { get; init; }
    [JsonPropertyName("pnl")] public required double Pnl { get; init; }
    [JsonPropertyName("currency")] public required string Currency { get; init; }
}

/// <summary>One live instance's open-position row inside
/// <see cref="EngineVenueState"/>.</summary>
public sealed record VenuePositionRow
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("side")] public required string Side { get; init; }
    [JsonPropertyName("volume")] public required double Volume { get; init; }
    /// <summary>Base-asset units (lots × contract size) — the GUI re-marks
    /// floating PnL as (price − entry) × units.</summary>
    [JsonPropertyName("units")] public required double Units { get; init; }
    [JsonPropertyName("entryPrice")] public required double EntryPrice { get; init; }
    [JsonPropertyName("currentPrice")] public required double CurrentPrice { get; init; }
    [JsonPropertyName("pnl")] public required double Pnl { get; init; }
    [JsonPropertyName("instance")] public required string Instance { get; init; }
}

/// <summary>engine.state.venue.{kind}.{venueId}: account + aggregated
/// positions of every live instance on the venue.</summary>
public sealed record EngineVenueState
{
    [JsonPropertyName("venueId")] public required string VenueId { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("account")] public required VenueAccountState Account { get; init; }
    [JsonPropertyName("positions")] public required IReadOnlyList<VenuePositionRow> Positions { get; init; }
}

/// <summary>engine.state.trading_mode: the engine-wide kill switch.</summary>
public sealed record TradingModeState
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("changed_at")] public required DateTimeOffset ChangedAt { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
}

/// <summary>One orphaned/unattributed broker position inside
/// <see cref="OrphansState"/>.</summary>
public sealed record OrphanPositionRow
{
    [JsonPropertyName("accountKey")] public required string AccountKey { get; init; }
    [JsonPropertyName("positionId")] public required long PositionId { get; init; }
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("side")] public required string Side { get; init; }
    [JsonPropertyName("quantity")] public required double Quantity { get; init; }
    [JsonPropertyName("entryPrice")] public required double EntryPrice { get; init; }
    [JsonPropertyName("label")] public required string? Label { get; init; }
    [JsonPropertyName("instance")] public required string? Instance { get; init; }
    [JsonPropertyName("firstSeenMs")] public required long FirstSeenMs { get; init; }
}

/// <summary>engine.state.orphans: broker positions no live instance owns.</summary>
public sealed record OrphansState
{
    [JsonPropertyName("policy")] public required string Policy { get; init; }
    [JsonPropertyName("graceSeconds")] public required int GraceSeconds { get; init; }
    [JsonPropertyName("orphans")] public required IReadOnlyList<OrphanPositionRow> Orphans { get; init; }
    [JsonPropertyName("unattributed")] public required IReadOnlyList<OrphanPositionRow> Unattributed { get; init; }
}

/// <summary>health.strategy.{instanceId}: one live instance's heartbeat.</summary>
public sealed record StrategyHealthState
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("strategy")] public required string Strategy { get; init; }
    [JsonPropertyName("symbol")] public required string Symbol { get; init; }
    [JsonPropertyName("tf")] public required string Tf { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("warming")] public required bool Warming { get; init; }
    [JsonPropertyName("error")] public required string? Error { get; init; }
    [JsonPropertyName("position")] public required int Position { get; init; }
    [JsonPropertyName("bars")] public required long Bars { get; init; }
    [JsonPropertyName("trades")] public required long Trades { get; init; }
    [JsonPropertyName("open_pnl")] public required double OpenPnl { get; init; }
    [JsonPropertyName("total_pnl")] public required double TotalPnl { get; init; }
    [JsonPropertyName("venues")] public required IReadOnlyList<string> Venues { get; init; }
    /// <summary>Kind of the SESSION venue row ("paper", "ctrader", …) —
    /// lets consumers pick the right engine.state.venue.{kind}.{id} subject
    /// when a paper session shares its venue row id with the broker subject.
    /// Omitted for sim-only sessions (additive; absent in older payloads).</summary>
    [JsonPropertyName("venue_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VenueKind { get; init; }
    [JsonPropertyName("ring_dropped")] public required long RingDropped { get; init; }
    [JsonPropertyName("ring_gaps")] public required long RingGaps { get; init; }
    [JsonPropertyName("ring_high_water")] public required long RingHighWater { get; init; }
}

/// <summary>Bus frame for one engine-state subject (the "engine.state"
/// plane, carried by MassTransit since P2): latest-per-subject GUI state.
/// <see cref="Json"/> holds the subject's payload (one of the records in
/// this file, serialized); null tombstones the subject.</summary>
public sealed record EngineStateChanged
{
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    [JsonPropertyName("json")] public required string? Json { get; init; }
}

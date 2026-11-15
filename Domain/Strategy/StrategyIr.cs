using System.Text.Json.Serialization;

namespace Core.Strategy;

/// <summary>
/// Strategy metadata for a live instance host. Historically the parsed
/// python/IR strategy definition; since the M9 legacy-path deletion the only
/// producer is the quant spawn path, which synthesizes it from the routing
/// table (LiveInstanceManager) — the package manifest overwrites the bar
/// facts during instance initialization.
///
/// The type keeps its historical name to avoid churn across the host
/// plumbing (live instance, sessions, market data).
/// </summary>
public sealed class StrategyIr
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
    [JsonPropertyName("timeframe")] public string Timeframe { get; set; } = "";

    /// <summary>Bar length in seconds (derived from the timeframe).</summary>
    [JsonPropertyName("bar_seconds")] public long BarSeconds { get; set; } = 60;

    /// <summary>"bar" (default) or "tick" — how the strategy is driven.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "bar";

    [JsonIgnore] public bool TickDriven => Mode == "tick";

    /// <summary>
    /// Historical bars to seed before the first live evaluation. The quant
    /// runtime overrides this with its own warmup requirement
    /// (QuantStrategyEvaluator.WarmupBars); this conservative default only
    /// applies when that is unavailable (extra bars are filtered harmlessly
    /// by the runtime's history window).
    /// </summary>
    public int WarmupBars() => 500;
}

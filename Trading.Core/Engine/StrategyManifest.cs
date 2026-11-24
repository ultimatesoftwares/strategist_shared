using System.IO.Compression;
using System.Text.Json;

namespace Trading.Core.Engine;

/// <summary>
/// The manifest a self-describing model package embeds as
/// <see cref="EntryName"/> inside its ML.NET model.zip. It declares how the
/// engine should trade the model — the decision policy, the cadence and the
/// tuning — so a new strategy ships as a data file: train, embed a manifest,
/// upload, deploy. No engine recompile.
///
/// <para>Configured under <see cref="StrategyKind.Model"/>; see
/// <see cref="ModelStrategies"/> for the policy registry that executes it.
/// Example:</para>
/// <code>
/// {
///   "version": 1,
///   "policy": "edge",        // registered policy name
///   "cadence": "ticks",      // "bars" (venue bars, default) | "ticks" (micro-bars)
///   "tickBarMs": 1000,       // micro-bar length; ticks cadence only
///   "entryBps": 10,          // policy entry tuning (0 = engine default)
///   "exitBps": 60,           // policy exit tuning (0 = engine default)
///   "takeProfitBps": 3,      // tick policies: take-profit distance (0 = disabled)
///   "entryZ": 2.0,           // edge policy: |Z60| entry gate (0 = engine default)
///   "exitZ": 0.25,           // edge policy: Z60 reversion exit gate (0 = engine default)
///   "maxHoldBars": 30,       // maximum hold in closed (micro-)bars
///   "orderQty": 1            // engine lots per entry
/// }
/// </code>
/// </summary>
public sealed record StrategyManifest
{
    /// <summary>Zip entry name of the manifest inside the model.zip.</summary>
    public const string EntryName = "strategy.json";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Manifest schema version; 1 is the only version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Decision policy name, resolved through <see cref="ModelStrategies"/>.</summary>
    public string Policy { get; init; } = "";

    /// <summary>"bars" (venue bars, the default) or "ticks" (fixed micro-bars).</summary>
    public string Cadence { get; init; } = "bars";

    /// <summary>Micro-bar length in milliseconds (ticks cadence only); 0 = engine default.</summary>
    public long TickBarMs { get; init; }

    /// <summary>Policy entry tuning in bps (e.g. minimum edge, or P(up) threshold); 0 = engine default.</summary>
    public long EntryBps { get; init; }

    /// <summary>Policy exit tuning in bps (e.g. stop loss, or P(up) exit threshold); 0 = engine default.</summary>
    public long ExitBps { get; init; }

    /// <summary>Tick policies: take-profit distance from the entry price in
    /// bps, checked per tick; 0 = disabled. The scalper policy requires it.</summary>
    public long TakeProfitBps { get; init; }

    /// <summary>Edge policy: |Z60| needed to enter; 0 = engine default (2.0).</summary>
    public double EntryZ { get; init; }

    /// <summary>Edge policy: exit once Z60 has reverted to within this of the mean; 0 = engine default (0.25).</summary>
    public double ExitZ { get; init; }

    /// <summary>Maximum holding time in closed (micro-)bars; 0 = engine default.</summary>
    public long MaxHoldBars { get; init; }

    /// <summary>Order size in engine lots; 0 = engine default (1).</summary>
    public ulong OrderQty { get; init; }

    /// <summary>Tuning for the <c>costScalper</c> policy; null = all defaults.
    /// Ignored by other policies.</summary>
    public CostScalperSettings? CostScalper { get; init; }

    /// <summary>True when the model wants tick micro-bars rather than venue bars.</summary>
    public bool IsTickCadence => string.Equals(Cadence, "ticks", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Read the manifest embedded in <paramref name="modelPath"/>. Null when
    /// the zip has no manifest entry; throws
    /// <see cref="InvalidOperationException"/> when the file is not a zip or
    /// the manifest is malformed or invalid.
    /// </summary>
    public static StrategyManifest? TryRead(string modelPath)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(modelPath);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidOperationException(
                $"{modelPath} is not an ML.NET model.zip", error);
        }
        using (zip)
        {
            if (zip.GetEntry(EntryName) is not { } entry)
                return null;
            StrategyManifest? manifest;
            try
            {
                using var stream = entry.Open();
                manifest = JsonSerializer.Deserialize<StrategyManifest>(stream, Json);
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    $"{modelPath}: invalid {EntryName}: {error.Message}", error);
            }
            if (manifest is null)
                throw new InvalidOperationException($"{modelPath}: {EntryName} is empty");
            manifest.Validate(modelPath);
            return manifest;
        }
    }

    /// <summary>
    /// Embed this manifest into an existing model.zip (replacing any previous
    /// manifest). ML.NET ignores unknown zip entries, so the model keeps
    /// loading normally. Exporters call this right after
    /// <c>ml.Model.Save(...)</c>.
    /// </summary>
    public void Embed(string modelPath)
    {
        Validate(modelPath);
        using var zip = ZipFile.Open(modelPath, ZipArchiveMode.Update);
        zip.GetEntry(EntryName)?.Delete();
        using var stream = zip.CreateEntry(EntryName).Open();
        JsonSerializer.Serialize(stream, this, Json);
    }

    private void Validate(string modelPath)
    {
        if (Version != 1)
            throw new InvalidOperationException(
                $"{modelPath}: unsupported {EntryName} version {Version} (this engine knows version 1)");
        if (string.IsNullOrWhiteSpace(Policy))
            throw new InvalidOperationException($"{modelPath}: {EntryName} needs a policy name");
        if (!IsTickCadence && !string.Equals(Cadence, "bars", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{modelPath}: {EntryName} cadence must be \"bars\" or \"ticks\" (got \"{Cadence}\")");
        if (!IsTickCadence && TickBarMs != 0)
            throw new InvalidOperationException(
                $"{modelPath}: {EntryName} tickBarMs only applies to the \"ticks\" cadence");
        if (IsTickCadence && TickBarMs != 0
            && TickBarMs is < TickEdgeStrategy.MinBarMs or > TickEdgeStrategy.MaxBarMs)
            throw new InvalidOperationException(
                $"{modelPath}: {EntryName} tickBarMs must be within "
                + $"{TickEdgeStrategy.MinBarMs}..={TickEdgeStrategy.MaxBarMs} ms (got {TickBarMs})");
        if (EntryBps < 0 || ExitBps < 0 || MaxHoldBars < 0 || TakeProfitBps < 0)
            throw new InvalidOperationException(
                $"{modelPath}: {EntryName} tuning values must be >= 0 (0 selects the engine default)");
        if (!double.IsFinite(EntryZ) || EntryZ is < 0 or > MlStrategy.MaxZGate
            || !double.IsFinite(ExitZ) || ExitZ is < 0 or > MlStrategy.MaxZGate)
            throw new InvalidOperationException(
                $"{modelPath}: {EntryName} entryZ/exitZ must be within 0..={MlStrategy.MaxZGate} "
                + "(0 selects the engine default)");
        CostScalper?.Validate(modelPath);
    }
}

/// <summary>
/// Tuning for the <c>costScalper</c> policy (see <c>CostScalperStrategy</c>).
/// Every field follows the manifest convention: 0 (or an absent field)
/// selects the engine default. All bps values are basis points of mid.
/// </summary>
public sealed record CostScalperSettings
{
    /// <summary>Required |predicted payoff| as a multiple of the live round-trip cost; 0 = 1.75.</summary>
    public double CostMultiple { get; init; }

    /// <summary>Commission estimate per order leg, in bps; venues do not report it. 0 = none.</summary>
    public double CommissionBps { get; init; }

    /// <summary>Efficiency-ratio boundary: below = fade regime, at/above = momentum regime; 0 = 0.35.</summary>
    public double TrendEr { get; init; }

    /// <summary>Max live spread as a multiple of its rolling median to enter; 0 = 1.5.</summary>
    public double SpreadCapRatio { get; init; }

    /// <summary>Minimum short-horizon volatility (sigma, bps) to enter; 0 = 0.5.</summary>
    public double VolFloorBps { get; init; }

    /// <summary>Maximum live sigma as a multiple of the slow typical-sigma EWMA to enter; 0 = 3.0.</summary>
    public double VolCeilRatio { get; init; }

    /// <summary>Take-profit target in entry sigmas (floored by cost × CostFloorMult); 0 = 2.0.</summary>
    public double TpAtrMult { get; init; }

    /// <summary>Take-profit floor as a multiple of round-trip cost (must stay &gt; 1); 0 = 1.5.</summary>
    public double CostFloorMult { get; init; }

    /// <summary>Stop distance in entry sigmas (clamped to cost floor and MaxStopBps); 0 = 3.0.</summary>
    public double StopAtrMult { get; init; }

    /// <summary>Fraction of peak floating net PnL surrendered before the profit lock closes; 0 = 0.4.</summary>
    public double TrailGiveback { get; init; }

    /// <summary>How much of the target decays away across the max hold (1 = all the way to the stale floor); 0 = 0.8.</summary>
    public double DecayRate { get; init; }

    /// <summary>Closed micro-bars after any exit before re-entry; 0 = 5.</summary>
    public long CooldownBars { get; init; }

    /// <summary>Closed micro-bars after a losing exit before re-entry; 0 = 15.</summary>
    public long LossCooldownBars { get; init; }

    /// <summary>Maximum entries per symbol per minute; 0 = 6.</summary>
    public long MaxTradesPerMin { get; init; }

    /// <summary>Consecutive losing trades per symbol before entries halt for the UTC day; 0 = 5.</summary>
    public long MaxConsecutiveLosses { get; init; }

    /// <summary>Realized loss per symbol per UTC day (bps, summed per trade) before entries halt; 0 = 500.</summary>
    public double MaxDailyLossBps { get; init; }

    internal void Validate(string modelPath)
    {
        static bool Bad(double v, double max) => !double.IsFinite(v) || v < 0 || v > max;
        if (Bad(CostMultiple, 10) || Bad(CommissionBps, 50) || Bad(TrendEr, 1)
            || Bad(SpreadCapRatio, 10) || Bad(VolFloorBps, 100) || Bad(VolCeilRatio, 20)
            || Bad(TpAtrMult, 10) || Bad(StopAtrMult, 20) || Bad(TrailGiveback, 0.95)
            || Bad(DecayRate, 1) || Bad(MaxDailyLossBps, 100_000))
            throw new InvalidOperationException(
                $"{modelPath}: {StrategyManifest.EntryName} costScalper tuning out of range "
                + "(all values must be finite, >= 0 and within their documented bounds; 0 = default)");
        if (CostFloorMult is not 0 && (!double.IsFinite(CostFloorMult) || CostFloorMult is <= 1 or > 5))
            throw new InvalidOperationException(
                $"{modelPath}: {StrategyManifest.EntryName} costScalper costFloorMult must be > 1 "
                + "(the take profit must clear the round-trip cost) and <= 5, or 0 for the default");
        if (CooldownBars < 0 || LossCooldownBars < 0 || MaxTradesPerMin < 0 || MaxConsecutiveLosses < 0)
            throw new InvalidOperationException(
                $"{modelPath}: {StrategyManifest.EntryName} costScalper bar/trade counts must be >= 0 (0 = default)");
    }
}

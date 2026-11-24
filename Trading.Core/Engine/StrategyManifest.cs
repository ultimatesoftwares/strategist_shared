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
    }
}

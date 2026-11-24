namespace Trading.Core.Engine;

/// <summary>
/// The open registry of decision policies behind self-describing model
/// packages (<see cref="StrategyKind.Model"/> + an embedded
/// <see cref="StrategyManifest"/>). The engine is not coupled to any single
/// strategy: a policy is just a named builder from (model, manifest,
/// operator overrides) to an <see cref="IStrategy"/>, and hosts may
/// <see cref="Register"/> additional policies without touching this library.
///
/// <para>Built-in policies:</para>
/// <list type="bullet">
/// <item><c>edge</c> — a named-feature regression predicting the payoff (bps)
/// of fading the Z60 extreme. Cadence "bars" runs <see cref="MlStrategy"/>'s
/// edge mode per venue bar; cadence "ticks" runs <see cref="TickEdgeStrategy"/>
/// over micro-bars with the tick-level stop. entryBps = minimum edge,
/// exitBps = stop loss, entryZ/exitZ = Z60 entry/reversion gates,
/// maxHoldBars = maximum hold.</item>
/// <item><c>probability</c> — a binary classifier over P(up), bars cadence
/// only (<see cref="MlStrategy"/>'s classifier rules). entryBps/exitBps are
/// P(up) thresholds in bps of probability. The tick-cadence classifier is
/// the experimental observe-only <see cref="TickMlStrategy"/> and must be
/// configured explicitly as kind tickMl, never through a manifest.</item>
/// </list>
///
/// <para>Operator overrides win: non-zero tuning in the
/// <see cref="StrategyConfig"/> beats the manifest, which beats the engine
/// defaults.</para>
/// </summary>
public static class ModelStrategies
{
    /// <summary>Builds the strategy for a manifest policy. Must throw
    /// <see cref="InvalidOperationException"/> with a descriptive message on
    /// any contract violation (wrong model output kind, bad tuning).</summary>
    public delegate IStrategy Builder(string modelPath, StrategyManifest manifest, StrategyConfig overrides);

    private static readonly Dictionary<string, Builder> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["edge"] = BuildEdge,
            ["probability"] = BuildProbability,
        };

    private static readonly Lock Gate = new();

    /// <summary>Registered policy names (for error messages and discovery).</summary>
    public static IReadOnlyCollection<string> Policies
    {
        get
        {
            lock (Gate)
                return Registry.Keys.ToArray();
        }
    }

    /// <summary>Register (or replace) a policy. Call at host startup, before
    /// any account is created.</summary>
    public static void Register(string policy, Builder builder)
    {
        if (string.IsNullOrWhiteSpace(policy))
            throw new ArgumentException("policy name must be non-empty", nameof(policy));
        lock (Gate)
            Registry[policy] = builder;
    }

    /// <summary>
    /// Build the strategy a self-describing model package asks for. Throws
    /// <see cref="InvalidOperationException"/> when the package has no
    /// manifest, names an unregistered policy, or violates the policy's
    /// model contract.
    /// </summary>
    public static IStrategy Create(StrategyConfig config)
    {
        var path = config.ModelPath!;
        var manifest = StrategyManifest.TryRead(path)
            ?? throw new InvalidOperationException(
                $"{path} does not embed a {StrategyManifest.EntryName} manifest; "
                + "export the model with one (StrategyManifest.Embed) or configure an explicit strategy kind (ml | tickMl | tickEdge)");
        Builder? builder;
        lock (Gate)
            Registry.TryGetValue(manifest.Policy, out builder);
        if (builder is null)
        {
            throw new InvalidOperationException(
                $"{path}: unknown strategy policy '{manifest.Policy}'; registered policies: "
                + string.Join(" | ", Policies.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)));
        }
        return builder(path, manifest, config);
    }

    /// <summary>Operator override beats manifest beats engine default (0).</summary>
    private static long Resolve(long overrideValue, long manifestValue) =>
        overrideValue > 0 ? overrideValue : manifestValue;

    private static ulong Resolve(ulong overrideValue, ulong manifestValue) =>
        overrideValue > 0 ? overrideValue : manifestValue;

    private static double Resolve(double overrideValue, double manifestValue) =>
        overrideValue > 0 ? overrideValue : manifestValue;

    private static IStrategy BuildEdge(
        string modelPath, StrategyManifest manifest, StrategyConfig overrides)
    {
        var entry = Resolve(overrides.EntryThresholdBps, manifest.EntryBps);
        var exit = Resolve(overrides.ExitThresholdBps, manifest.ExitBps);
        var hold = Resolve(overrides.MaxHoldBars, manifest.MaxHoldBars);
        var qty = Resolve(overrides.OrderQty, manifest.OrderQty);
        var entryZ = Resolve(overrides.EntryZ, manifest.EntryZ);
        var exitZ = Resolve(overrides.ExitZ, manifest.ExitZ);
        if (manifest.IsTickCadence)
        {
            return TickEdgeStrategy.FromFile(
                modelPath, qty, entry, exit, hold,
                Resolve(overrides.TickBarMs, manifest.TickBarMs), entryZ, exitZ);
        }
        var strategy = MlStrategy.FromFile(modelPath, qty, entry, exit, hold, entryZ, exitZ);
        if (!strategy.IsEdgeModel)
        {
            strategy.Dispose();
            throw new InvalidOperationException(
                $"{modelPath}: policy 'edge' needs a named-feature regression model, but this model is a classifier (use policy 'probability')");
        }
        return strategy;
    }

    private static IStrategy BuildProbability(
        string modelPath, StrategyManifest manifest, StrategyConfig overrides)
    {
        if (manifest.IsTickCadence)
        {
            throw new InvalidOperationException(
                $"{modelPath}: policy 'probability' over ticks is the experimental observe-only tickMl strategy; configure strategy kind tickMl explicitly instead of a manifest");
        }
        var strategy = MlStrategy.FromFile(
            modelPath,
            Resolve(overrides.OrderQty, manifest.OrderQty),
            Resolve(overrides.EntryThresholdBps, manifest.EntryBps),
            Resolve(overrides.ExitThresholdBps, manifest.ExitBps),
            Resolve(overrides.MaxHoldBars, manifest.MaxHoldBars));
        if (strategy.IsEdgeModel)
        {
            strategy.Dispose();
            throw new InvalidOperationException(
                $"{modelPath}: policy 'probability' needs a classifier, but this model is a named-feature regression (use policy 'edge')");
        }
        return strategy;
    }
}

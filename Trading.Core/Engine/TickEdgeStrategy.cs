namespace Trading.Core.Engine;

/// <summary>
/// The high-frequency edge strategy: top-of-book quote ticks are aggregated
/// into fixed-length micro-bars (default 1 second) and every closed micro-bar
/// runs <see cref="MlStrategy"/>'s edge rules over a named-feature regression
/// model, so the mean-reversion research loop (fade the Z60 extreme when the
/// predicted payoff clears the minimum edge) drives live order flow at tick
/// cadence instead of venue-bar cadence.
///
/// <para><b>Model contract</b>: a named-feature regression over
/// <see cref="BarFeatures"/> whose <c>Score</c> is the predicted payoff in
/// bps of fading the Z60 extreme (<see cref="ModelOutputKind.Edge"/> — the
/// same model.zip the bar-mode <see cref="MlStrategy"/> edge mode takes).
/// Classifiers and returns-vector models are rejected at load.</para>
///
/// <para><b>Rules</b>:</para>
/// <list type="bullet">
/// <item>Each tick lands in a micro-bar bucket of <see cref="BarMs"/>
/// milliseconds (by tick timestamp); the bucket's last mid closes the
/// micro-bar when a tick arrives in a later bucket. The tick count is the
/// bar volume.</item>
/// <item>Warm-up needs <see cref="BarFeatures.Warmup"/> + 1 closed
/// micro-bars per symbol, built from live ticks (venue bar history cannot
/// warm micro-bars, so <see cref="WarmupBars"/> is 0).</item>
/// <item>Per closed micro-bar: cancel working orders and wait, otherwise
/// enter against the stretch when flat (|Z60| ≥ <see cref="MlStrategy.EdgeEntryZ"/>
/// and Score ≥ the minimum edge; short above the mean, long below) or
/// flatten once Z60 reverts within <see cref="MlStrategy.EdgeExitZ"/>, after
/// the maximum hold, or at the stop loss. Orders go marketable at the
/// current tick's touch.</item>
/// <item><b>Every tick</b> while holding: flatten immediately when the mid
/// moves the stop-loss distance against the entry price — the stop does not
/// wait for the micro-bar to close.</item>
/// <item>An invalid quote or a processing gap resets the symbol's micro-bars
/// (stop and max-hold exits keep working while the window re-warms); an
/// inference failure poisons the strategy: cancel everything, never trade
/// again (<see cref="DecisionStatus.Degraded"/>).</item>
/// </list>
/// </summary>
public sealed class TickEdgeStrategy : IStrategy, IDisposable
{
    /// <summary>Default micro-bar length in milliseconds.</summary>
    public const long DefaultBarMs = 1_000;

    /// <summary>Shortest allowed micro-bar, in milliseconds.</summary>
    public const long MinBarMs = 100;

    /// <summary>Longest allowed micro-bar (one hour), in milliseconds.</summary>
    public const long MaxBarMs = 3_600_000;

    private sealed class SymbolState
    {
        public readonly BarFeatureWindow Window = new();

        /// <summary>Open time of the micro-bar being built; MinValue = none yet.</summary>
        public long BucketStartMs = long.MinValue;

        /// <summary>Last mid seen in the open micro-bar (its close-so-far).</summary>
        public double LastMid;

        /// <summary>Ticks folded into the open micro-bar (the bar volume).</summary>
        public ulong TickCount;

        /// <summary>Closed micro-bars seen while holding the current position.</summary>
        public long BarsHeld;

        /// <summary>Price of the order that opened the position.</summary>
        public long EntryPrice;
    }

    private readonly IModelPredictor _predictor;
    private readonly ulong _orderQty;
    private readonly double _minEdgeBps;
    private readonly double _stopBps;
    private readonly long _maxHoldBars;
    private readonly IReadOnlyList<int> _featureIndexes;
    private readonly Dictionary<uint, SymbolState> _state = [];
    // Reused per event: the engine is single-threaded by contract.
    private readonly float[] _features;
    private readonly double[] _barFeatures = new double[BarFeatures.Names.Count];

    /// <summary>Set on inference failure: cancel everything, never trade again.</summary>
    private bool _poisoned;
    private StrategyDecision? _lastDecision;

    /// <summary>
    /// Build the strategy over an already-loaded predictor. All parameters
    /// must be resolved (no zeros): see <see cref="FromFile"/> for the
    /// defaulting entry point.
    /// </summary>
    public TickEdgeStrategy(
        IModelPredictor predictor, ulong orderQty, long minEdgeBps, long stopBps,
        long maxHoldBars, long barMs)
    {
        if (predictor.OutputKind != ModelOutputKind.Edge || predictor.FeatureIndexes is null)
            throw new InvalidOperationException(
                "tickEdge requires a named-feature regression (edge) model over BarFeatures");
        if (barMs is < MinBarMs or > MaxBarMs)
            throw new InvalidOperationException(
                $"tickEdge micro-bar length must be within {MinBarMs}..={MaxBarMs} ms (got {barMs})");
        if (orderQty is 0 or > long.MaxValue || minEdgeBps <= 0 || stopBps <= 0 || maxHoldBars <= 0)
            throw new InvalidOperationException(
                "tickEdge requires a positive order qty, minimum edge, stop loss and max hold");
        _predictor = predictor;
        _orderQty = orderQty;
        _minEdgeBps = minEdgeBps;
        _stopBps = stopBps;
        _maxHoldBars = maxHoldBars;
        BarMs = barMs;
        _featureIndexes = predictor.FeatureIndexes;
        _features = new float[predictor.FeatureCount];
    }

    /// <summary>
    /// Load the model and build the strategy. 0 parameters select the
    /// defaults: order qty 1, minimum edge <see cref="MlStrategy.DefaultMinEdgeBps"/>,
    /// stop <see cref="MlStrategy.DefaultStopBps"/>, max hold
    /// <see cref="MlStrategy.DefaultEdgeMaxHoldBars"/> micro-bars,
    /// micro-bar length <see cref="DefaultBarMs"/> ms.
    /// </summary>
    public static TickEdgeStrategy FromFile(
        string path, ulong orderQty, long minEdgeBps, long stopBps, long maxHoldBars,
        long tickBarMs)
    {
        IModelPredictor predictor;
        try
        {
            predictor = ModelPredictor.Load(path);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"failed to load model {path}: {error.Message}", error);
        }
        try
        {
            return new TickEdgeStrategy(
                predictor,
                orderQty > 0 ? orderQty : 1,
                minEdgeBps > 0 ? minEdgeBps : MlStrategy.DefaultMinEdgeBps,
                stopBps > 0 ? stopBps : MlStrategy.DefaultStopBps,
                maxHoldBars > 0 ? maxHoldBars : MlStrategy.DefaultEdgeMaxHoldBars,
                tickBarMs > 0 ? tickBarMs : DefaultBarMs);
        }
        catch
        {
            predictor.Dispose();
            throw;
        }
    }

    /// <summary>Micro-bar length in milliseconds.</summary>
    public long BarMs { get; }

    /// <summary>Minimum predicted edge to enter, in bps.</summary>
    public double MinEdgeBps => _minEdgeBps;

    /// <summary>Stop loss from the entry price, in bps.</summary>
    public double StopBps => _stopBps;

    /// <summary>Maximum holding time in closed micro-bars.</summary>
    public long MaxHoldBars => _maxHoldBars;

    public bool RequiresAtomicQuotes => true;

    public StrategyDecision? LastDecision => _lastDecision;

    /// <summary>Venue-interval bar history cannot warm micro-bars: the window
    /// warms from live ticks only (see the class remarks).</summary>
    public int WarmupBars => 0;

    /// <summary>Tick-driven: book deltas carry no extra signal.</summary>
    public void OnBook(in StrategyContext ctx, List<Intent> output)
    {
    }

    public void OnQuoteGap(in StrategyContext ctx, List<Intent> output)
    {
        // Rebuild the micro-bars from scratch, but keep the position's entry
        // price and hold count so the stop and max-hold exits survive the gap.
        if (_state.TryGetValue(ctx.SymbolId, out var stale))
        {
            _state[ctx.SymbolId] = new SymbolState
            {
                BarsHeld = stale.BarsHeld,
                EntryPrice = stale.EntryPrice,
            };
        }
        if (!_poisoned)
        {
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                "Quote stream interrupted or invalid; micro-bars must warm up again.");
        }
        CancelAll(ctx, output);
    }

    public void OnTick(in StrategyContext ctx, in QuoteTick tick, List<Intent> output)
    {
        if (_poisoned)
        {
            CancelAll(ctx, output);
            return;
        }
        if (tick.Bid <= 0 || tick.Ask <= tick.Bid)
        {
            OnQuoteGap(ctx, output);
            return;
        }

        if (!_state.TryGetValue(ctx.SymbolId, out var state))
            _state[ctx.SymbolId] = state = new SymbolState();

        // Halves avoid overflow on scaled-long extremes.
        var mid = (tick.Bid / 2.0) + (tick.Ask / 2.0);
        var tsMs = (long)(tick.TsNanos / 1_000_000UL);
        var bucket = tsMs - tsMs % BarMs;

        if (state.BucketStartMs == long.MinValue)
        {
            state.BucketStartMs = bucket;
        }
        else if (bucket > state.BucketStartMs)
        {
            OnMicroBarClose(ctx, tick, state, output);
            if (_poisoned)
                return;
            state.BucketStartMs = bucket;
            state.TickCount = 0;
        }
        else if (bucket < state.BucketStartMs)
        {
            return; // Out-of-order tick: ignore.
        }

        state.LastMid = mid;
        state.TickCount++;

        // Tick-level stop loss: don't wait for the micro-bar to close. A
        // working order (entry fill pending, or an exit already on its way)
        // defers to the micro-bar-close cancel-then-decide cycle.
        if (output.Count == 0 && ctx.Position != 0 && ctx.OpenOrders.Count == 0
            && state.EntryPrice > 0 && mid > 0
            && Math.Sign(ctx.Position) * Math.Log(mid / state.EntryPrice) <= -_stopBps / 1e4)
        {
            _lastDecision = new StrategyDecision(
                _lastDecision?.Forecast ?? double.NaN, DecisionStatus.Active,
                "Intrabar stop loss; flattening.", SignalKind: "edgeBps",
                EntryThreshold: _minEdgeBps, ZThreshold: MlStrategy.EdgeEntryZ);
            PlaceTarget(ctx, tick, state, 0, output);
        }
    }

    /// <summary>Fold the finished micro-bar into the window and decide.</summary>
    private void OnMicroBarClose(
        in StrategyContext ctx, in QuoteTick tick, SymbolState state, List<Intent> output)
    {
        // Tick count stands in for volume (VolumeRatio stays meaningful).
        var warm = state.Window.Add(state.BucketStartMs, state.LastMid, state.TickCount)
            && state.Window.IsWarm;

        if (ctx.Position == 0)
            state.BarsHeld = 0;
        else
            state.BarsHeld++;

        if (!warm || !ComputeFeatures(state))
        {
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup,
                "Waiting for sufficient micro-bars, or ignoring a stale bucket.");
            // Model-free exits keep working while the window (re)warms.
            if (ctx.Position != 0 && state.BarsHeld >= _maxHoldBars)
            {
                if (ctx.OpenOrders.Count > 0)
                {
                    CancelAll(ctx, output);
                    return;
                }
                _lastDecision = _lastDecision.Value with
                {
                    Reason = "Maximum hold reached while the window warms; flattening.",
                };
                PlaceTarget(ctx, tick, state, 0, output);
            }
            return;
        }

        ModelPrediction prediction;
        try
        {
            prediction = _predictor.Predict(_features);
        }
        catch (Exception)
        {
            _poisoned = true;
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                "Model inference failed; trading is disabled.");
            CancelAll(ctx, output);
            return;
        }
        if (!float.IsFinite(prediction.Score))
        {
            _poisoned = true;
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                "Model returned a non-finite score; trading is disabled.");
            CancelAll(ctx, output);
            return;
        }

        var z = _barFeatures[BarFeatures.Z60];
        _lastDecision = new StrategyDecision(prediction.Score, DecisionStatus.Active,
            SignalKind: "edgeBps", Z60: z, EntryThreshold: _minEdgeBps,
            ZThreshold: MlStrategy.EdgeEntryZ);

        if (ctx.OpenOrders.Count > 0)
        {
            _lastDecision = _lastDecision.Value with
            {
                Reason = "Cancelling working orders before the next decision.",
            };
            CancelAll(ctx, output);
            return;
        }

        var target = EdgeTarget(ctx, state, prediction.Score);
        _lastDecision = _lastDecision.Value with
        {
            Reason = ctx.Position == 0
                ? Math.Abs(z) < MlStrategy.EdgeEntryZ && prediction.Score < _minEdgeBps
                    ? "Z60 and predicted edge are below entry thresholds."
                : Math.Abs(z) < MlStrategy.EdgeEntryZ ? "Z60 is below the entry threshold."
                : prediction.Score < _minEdgeBps ? "Predicted edge is below the entry threshold."
                : target > 0 ? "Long entry signal." : "Short entry signal."
                : target == 0 ? "Exit signal: reversion, stop or maximum hold."
                : "Holding position; no exit condition met.",
        };
        PlaceTarget(ctx, tick, state, target, output);
    }

    /// <summary>Edge-mode target position (mirrors <see cref="MlStrategy"/>).</summary>
    private long EdgeTarget(in StrategyContext ctx, SymbolState state, float score)
    {
        var z = _barFeatures[BarFeatures.Z60];
        if (ctx.Position == 0)
        {
            if (Math.Abs(z) < MlStrategy.EdgeEntryZ || score < _minEdgeBps)
                return 0;
            return z > 0 ? -(long)_orderQty : (long)_orderQty;
        }

        var direction = Math.Sign(ctx.Position);
        var reverted = direction * z >= -MlStrategy.EdgeExitZ;
        var heldTooLong = state.BarsHeld >= _maxHoldBars;
        var stopped = state.EntryPrice > 0 && state.LastMid > 0
            && direction * Math.Log(state.LastMid / state.EntryPrice) <= -_stopBps / 1e4;
        return reverted || heldTooLong || stopped ? 0 : ctx.Position;
    }

    /// <summary>Fills <see cref="_features"/> from the warm window; false when any value is non-finite.</summary>
    private bool ComputeFeatures(SymbolState state)
    {
        state.Window.Compute(_barFeatures);
        for (var i = 0; i < _features.Length; i++)
        {
            var value = (float)_barFeatures[_featureIndexes[i]];
            if (!float.IsFinite(value))
                return false;
            _features[i] = value;
        }
        return true;
    }

    /// <summary>Place the delta to <paramref name="target"/> marketable at the tick's touch.</summary>
    private static void PlaceTarget(
        in StrategyContext ctx, in QuoteTick tick, SymbolState state, long target,
        List<Intent> output)
    {
        var delta = target - ctx.Position;
        if (delta == 0)
            return;
        var side = delta > 0 ? Side.Buy : Side.Sell;
        var price = side == Side.Buy ? tick.Ask : tick.Bid;
        if (price <= 0)
            return;
        if (ctx.Position == 0)
            state.EntryPrice = price;
        output.Add(new Intent.Place(ctx.SymbolId, side, price, (ulong)Math.Abs(delta)));
    }

    private static void CancelAll(in StrategyContext ctx, List<Intent> output)
    {
        foreach (var order in ctx.OpenOrders)
            output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
    }

    public void Dispose() => _predictor.Dispose();
}

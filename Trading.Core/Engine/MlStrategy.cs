namespace Trading.Core.Engine;

/// <summary>
/// The model-driven bar strategy: an ML.NET binary classifier over
/// close-to-close returns, with mt5-bot-style entry/exit rules.
///
/// <para><b>Model contract</b>: an ML.NET <c>model.zip</c> — see
/// <see cref="MlNetPredictor"/>. The default input is a vector of
/// close-to-close returns (newest first); named <see cref="BarFeatures"/>
/// columns are also supported.</para>
///
/// <para><b>Rules (per closed bar)</b>:</para>
/// <list type="bullet">
/// <item>Warm up until FeatureCount + 1 consecutive closes are seen; a gap
/// (missing bar / interval change) resets the window.</item>
/// <item>P(up) ≥ entry threshold wants long, P(up) ≤ 1 − entry threshold
/// wants short.</item>
/// <item>Flat + entry signal: place the order qty at the touch (buy at ask,
/// sell at bid — marketable; falls back to the bar close when the book is
/// empty).</item>
/// <item>Holding: flatten (target 0, never reverse in one step) when the
/// signal reverses, fades through the exit threshold, or the position has
/// been held for the max hold bars.</item>
/// <item>Open orders at bar close: cancel them and hold — placement waits
/// for a later bar.</item>
/// <item>An inference failure poisons the strategy: it cancels everything
/// and never trades again (the host sees
/// <see cref="DecisionStatus.Degraded"/>).</item>
/// </list>
///
/// <para><b>Named-feature models</b>: an ML.NET model over
/// <see cref="BarFeatures"/> is fed those features once
/// <see cref="BarFeatures.Warmup"/> + 1 bars are seen. A classifier keeps the
/// P(up) rules above. A regression <c>Score</c> is the predicted payoff in
/// bps of fading the <c>Z60</c> extreme (<see cref="ModelOutputKind.Edge"/>):
/// enter when flat, |Z60| ≥ <see cref="EdgeEntryZ"/> and Score ≥ the minimum
/// edge, short above the mean and long below; flatten once Z60 reverts
/// within <see cref="EdgeExitZ"/>, after the max hold bars, or at the stop
/// loss. In this mode the entry threshold is the minimum edge in bps, the
/// exit threshold is the stop loss in bps from the entry price.</para>
/// </summary>
public sealed class MlStrategy : IStrategy, IDisposable
{
    /// <summary>Default P(up) entry threshold in bps of probability (6000 = 0.60).</summary>
    public const long DefaultEntryThresholdBps = 6_000;

    /// <summary>Default P(up) exit threshold in bps of probability (5000 = 0.50).</summary>
    public const long DefaultExitThresholdBps = 5_000;

    /// <summary>Default maximum holding time in closed bars.</summary>
    public const long DefaultMaxHoldBars = 5;

    /// <summary>Edge mode: default minimum predicted payoff to enter, in bps.</summary>
    public const long DefaultMinEdgeBps = 10;

    /// <summary>Edge mode: default stop loss from the entry price, in bps.</summary>
    public const long DefaultStopBps = 60;

    /// <summary>Edge mode: default maximum holding time in closed bars.</summary>
    public const long DefaultEdgeMaxHoldBars = 30;

    /// <summary>Edge mode: |Z60| needed to enter.</summary>
    public const double EdgeEntryZ = 2.0;

    /// <summary>Edge mode: exit once Z60 has reverted to within this of the mean.</summary>
    public const double EdgeExitZ = 0.25;

    private sealed class SymbolState
    {
        /// <summary>(bar open time ms, close) oldest → newest; capped at FeatureCount + 1.</summary>
        public readonly List<(long TimeMs, double Close)> Closes = [];

        /// <summary>Bar interval inferred from the first two consecutive bars.</summary>
        public long? IntervalMs;

        /// <summary>Closed bars seen while holding the current position.</summary>
        public long BarsHeld;

        /// <summary>Named-feature history (named-feature models only).</summary>
        public BarFeatureWindow? Window;

        /// <summary>Edge mode: price of the order that opened the position.</summary>
        public long EntryPrice;
    }

    private readonly IModelPredictor _predictor;
    private readonly ulong _orderQty;
    private readonly float _entryThreshold;
    private readonly float _exitThreshold;
    private readonly long _maxHoldBars;
    private readonly IReadOnlyList<int>? _featureIndexes;
    private readonly bool _edge;
    private readonly double _minEdgeBps;
    private readonly double _stopBps;
    private readonly Dictionary<uint, SymbolState> _state = [];
    // Reused per bar: the engine is single-threaded by contract.
    private readonly float[] _features;
    private readonly double[] _returns = new double[ModelPredictor.ReturnCount];
    private readonly double[] _barFeatures = new double[BarFeatures.Names.Count];

    /// <summary>Set on inference failure: cancel everything, never trade again.</summary>
    private bool _poisoned;
    private StrategyDecision? _lastDecision;

    private MlStrategy(
        IModelPredictor predictor, ulong orderQty, float entryThreshold, float exitThreshold,
        long maxHoldBars, double minEdgeBps = 0, double stopBps = 0)
    {
        _predictor = predictor;
        _orderQty = orderQty;
        _entryThreshold = entryThreshold;
        _exitThreshold = exitThreshold;
        _maxHoldBars = maxHoldBars;
        _featureIndexes = predictor.FeatureIndexes;
        _edge = predictor.OutputKind == ModelOutputKind.Edge;
        _minEdgeBps = minEdgeBps;
        _stopBps = stopBps;
        _features = new float[predictor.FeatureCount];
    }

    /// <summary>True for a named-feature regression model (see the class remarks).</summary>
    public bool IsEdgeModel => _edge;

    /// <summary>Closed bars needed before the first prediction.</summary>
    public int WarmupBars => _featureIndexes is null ? ModelPredictor.ReturnCount + 1 : BarFeatures.Warmup + 1;

    /// <summary>
    /// Load the model and build the strategy. 0 (or negative) parameters
    /// select the defaults; thresholds are bps of probability (6000 = 0.60).
    /// </summary>
    public static MlStrategy FromFile(
        string path, ulong orderQty, long entryThresholdBps, long exitThresholdBps,
        long maxHoldBars)
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

        if (predictor.OutputKind == ModelOutputKind.Edge)
        {
            return new MlStrategy(
                predictor,
                orderQty > 0 ? orderQty : 1,
                0, 0,
                maxHoldBars > 0 ? maxHoldBars : DefaultEdgeMaxHoldBars,
                entryThresholdBps > 0 ? entryThresholdBps : DefaultMinEdgeBps,
                exitThresholdBps > 0 ? exitThresholdBps : DefaultStopBps);
        }

        var entry = entryThresholdBps > 0 ? entryThresholdBps : DefaultEntryThresholdBps;
        var exit = exitThresholdBps > 0 ? exitThresholdBps : DefaultExitThresholdBps;
        if (entry is < 1 or > 10_000 || exit is < 1 or > 10_000)
        {
            predictor.Dispose();
            throw new InvalidOperationException(
                $"ml thresholds must be within 1..=10000 bps (entry {entry}, exit {exit})");
        }

        return new MlStrategy(
            predictor,
            orderQty > 0 ? orderQty : 1,
            entry / 10_000.0f,
            exit / 10_000.0f,
            maxHoldBars > 0 ? maxHoldBars : DefaultMaxHoldBars);
    }

    public StrategyDecision? LastDecision => _lastDecision;

    /// <summary>History warm-up: fold the bar into the feature window only.</summary>
    public void WarmBar(in Bar bar)
    {
        if (_poisoned)
            return;
        if (!_state.TryGetValue(bar.SymbolId, out var state))
        {
            state = new SymbolState();
            _state[bar.SymbolId] = state;
        }
        UpdateWindow(state, bar, _features);
    }

    /// <summary>Bar-driven: book/tick events carry no signal for this strategy.</summary>
    public void OnBook(in StrategyContext ctx, List<Intent> output)
    {
    }

    public void OnBar(in StrategyContext ctx, in Bar bar, List<Intent> output)
    {
        if (_poisoned)
        {
            CancelAll(ctx, output);
            return;
        }

        if (!_state.TryGetValue(bar.SymbolId, out var state))
        {
            state = new SymbolState();
            _state[bar.SymbolId] = state;
        }
        if (!UpdateWindow(state, bar, _features))
        {
            var (warmCount, warmTarget) = WarmupProgress(state);
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup,
                "Waiting for sufficient valid bars, or ignoring a stale bar.",
                WarmupCount: warmCount, WarmupTarget: warmTarget);
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
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded, "Model inference failed; trading is disabled.");
            CancelAll(ctx, output);
            return;
        }
        if (_edge && !float.IsFinite(prediction.Score))
        {
            _poisoned = true;
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded, "Model returned a non-finite score; trading is disabled.");
            CancelAll(ctx, output);
            return;
        }
        var up = prediction.ProbabilityUp;
        _lastDecision = _edge
            ? new StrategyDecision(prediction.Score, DecisionStatus.Active, SignalKind: "edgeBps",
                Z60: _barFeatures[BarFeatures.Z60], EntryThreshold: _minEdgeBps, ZThreshold: EdgeEntryZ)
            : new StrategyDecision(up, DecisionStatus.Active, SignalKind: "probabilityUp", EntryThreshold: _entryThreshold);

        if (ctx.Position == 0)
            state.BarsHeld = 0;
        else
            state.BarsHeld++;

        // Working orders at bar close: cancel and decide on a later bar
        // (mirrors mt5-bot's "pending order on instrument" hold).
        if (ctx.OpenOrders.Count > 0)
        {
            _lastDecision = _lastDecision.Value with { Reason = "Cancelling working orders before the next decision." };
            CancelAll(ctx, output);
            return;
        }

        if (_edge)
        {
            var edgeTarget = EdgeTarget(ctx, bar, state, prediction.Score);
            var z = _barFeatures[BarFeatures.Z60];
            var reason = ctx.Position == 0
                ? Math.Abs(z) < EdgeEntryZ && prediction.Score < _minEdgeBps ? "Z60 and predicted edge are below entry thresholds."
                : Math.Abs(z) < EdgeEntryZ ? "Z60 is below the entry threshold."
                : prediction.Score < _minEdgeBps ? "Predicted edge is below the entry threshold."
                : edgeTarget > 0 ? "Long entry signal." : "Short entry signal."
                : edgeTarget == 0 ? "Exit signal: reversion, stop or maximum hold." : "Holding position; no exit condition met.";
            _lastDecision = _lastDecision.Value with { Reason = reason };
            Place(ctx, bar, state, edgeTarget, output);
            return;
        }

        Side? entrySide = up >= _entryThreshold ? Side.Buy
            : up <= 1.0f - _entryThreshold ? Side.Sell
            : null;

        long target;
        if (ctx.Position == 0)
        {
            target = entrySide switch
            {
                Side.Buy => (long)_orderQty,
                Side.Sell => -(long)_orderQty,
                _ => 0,
            };
        }
        else
        {
            var isLong = ctx.Position > 0;
            var heldTooLong = state.BarsHeld >= _maxHoldBars;
            var reversed = entrySide is { } side && (side == Side.Buy) != isLong;
            var faded = isLong ? up < _exitThreshold : up > 1.0f - _exitThreshold;
            // Flatten only; re-enter on a later bar.
            target = heldTooLong || reversed || faded ? 0 : ctx.Position;
        }

        _lastDecision = _lastDecision.Value with
        {
            Reason = target == ctx.Position ? "No position change indicated."
                : target == 0 ? "Exit signal." : target > 0 ? "Long entry signal." : "Short entry signal."
        };
        Place(ctx, bar, state, target, output);
    }

    /// <summary>Edge-mode target position (see the class remarks).</summary>
    private long EdgeTarget(in StrategyContext ctx, in Bar bar, SymbolState state, float score)
    {
        var z = _barFeatures[BarFeatures.Z60];
        if (ctx.Position == 0)
        {
            if (Math.Abs(z) < EdgeEntryZ || score < _minEdgeBps)
                return 0;
            return z > 0 ? -(long)_orderQty : (long)_orderQty;
        }

        var direction = Math.Sign(ctx.Position);
        var reverted = direction * z >= -EdgeExitZ;
        var heldTooLong = state.BarsHeld >= _maxHoldBars;
        var stopped = state.EntryPrice > 0 && bar.Close > 0
            && direction * Math.Log((double)bar.Close / state.EntryPrice) <= -_stopBps / 1e4;
        return reverted || heldTooLong || stopped ? 0 : ctx.Position;
    }

    private static void Place(
        in StrategyContext ctx, in Bar bar, SymbolState state, long target, List<Intent> output)
    {
        var delta = target - ctx.Position;
        if (delta == 0)
            return;
        var orderSide = delta > 0 ? Side.Buy : Side.Sell;
        // Marketable at the touch; bar close when the book is empty.
        var price = orderSide == Side.Buy
            ? ctx.Book.TryGetBestAsk(out var ask, out _) ? ask : bar.Close
            : ctx.Book.TryGetBestBid(out var bid, out _) ? bid : bar.Close;
        if (price <= 0)
            return;
        if (ctx.Position == 0)
            state.EntryPrice = price;
        output.Add(new Intent.Place(
            ctx.SymbolId, orderSide, price, (ulong)Math.Abs(delta)));
    }

    private static void CancelAll(in StrategyContext ctx, List<Intent> output)
    {
        foreach (var order in ctx.OpenOrders)
            output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
    }

    /// <summary>Bars accumulated towards the first decision, and the bars
    /// needed, for whichever window flavour this model uses.</summary>
    private (int Count, int Target) WarmupProgress(SymbolState state) =>
        _featureIndexes is not null
            ? (state.Window?.Count ?? 0, BarFeatureWindow.WarmTarget)
            : (state.Closes.Count, ModelPredictor.ReturnCount + 1);

    /// <summary>
    /// Fold a closed bar into the symbol's return window. Fills
    /// <paramref name="features"/> and returns true once the window is warm;
    /// false while warming up (or after a reset on gaps / bad closes).
    /// </summary>
    private bool UpdateWindow(SymbolState state, in Bar bar, float[] features)
    {
        if (_featureIndexes is not null)
            return UpdateFeatures(state, bar, features);

        if (bar.Close <= 0)
        {
            state.Closes.Clear();
            state.IntervalMs = null;
            return false;
        }
        if (state.Closes.Count > 0)
        {
            var delta = bar.TimeUnixMs - state.Closes[^1].TimeMs;
            if (delta <= 0)
                return false; // Stale or duplicate bar: ignore.
            if (state.IntervalMs is null)
            {
                state.IntervalMs = delta;
            }
            else if (delta != state.IntervalMs)
            {
                // Gap or interval change: consecutive closes only.
                state.Closes.Clear();
                state.IntervalMs = null;
            }
        }
        state.Closes.Add((bar.TimeUnixMs, bar.Close));
        if (state.Closes.Count > ModelPredictor.ReturnCount + 1)
            state.Closes.RemoveAt(0);
        if (state.Closes.Count < ModelPredictor.ReturnCount + 1)
            return false;

        for (var i = 0; i < ModelPredictor.ReturnCount; i++)
            _returns[i] = state.Closes[i + 1].Close / state.Closes[i].Close - 1.0;
        return ModelPredictor.TryFeaturesFromReturns(_returns, features);
    }

    private bool UpdateFeatures(SymbolState state, in Bar bar, float[] features)
    {
        state.Window ??= new BarFeatureWindow();
        if (!state.Window.Add(bar.TimeUnixMs, bar.Close, bar.Volume) || !state.Window.IsWarm)
            return false;
        state.Window.Compute(_barFeatures);
        for (var i = 0; i < features.Length; i++)
        {
            var value = (float)_barFeatures[_featureIndexes![i]];
            if (!float.IsFinite(value))
                return false;
            features[i] = value;
        }
        return true;
    }

    public void Dispose() => _predictor.Dispose();
}

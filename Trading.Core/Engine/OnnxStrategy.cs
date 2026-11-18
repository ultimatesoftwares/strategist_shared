namespace Trading.Core.Engine;

/// <summary>
/// The model-driven bar strategy: ONNX binary classifier over close-to-close
/// returns, with mt5-bot-style entry/exit rules.
///
/// <para><b>Model contract</b>: see <see cref="OnnxPredictor"/> — one
/// <c>f32[1, FeatureCount]</c> input of returns (newest first), outputs
/// <c>label</c> (<c>i64[1]</c>) and <c>probabilities</c> (<c>f32[1, 2]</c>,
/// <c>[P(down), P(up)]</c>).</para>
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
/// </summary>
public sealed class OnnxStrategy : IStrategy, IDisposable
{
    /// <summary>Default P(up) entry threshold in bps of probability (6000 = 0.60).</summary>
    public const long DefaultEntryThresholdBps = 6_000;

    /// <summary>Default P(up) exit threshold in bps of probability (5000 = 0.50).</summary>
    public const long DefaultExitThresholdBps = 5_000;

    /// <summary>Default maximum holding time in closed bars.</summary>
    public const long DefaultMaxHoldBars = 5;

    private sealed class SymbolState
    {
        /// <summary>(bar open time ms, close) oldest → newest; capped at FeatureCount + 1.</summary>
        public readonly List<(long TimeMs, double Close)> Closes = [];

        /// <summary>Bar interval inferred from the first two consecutive bars.</summary>
        public long? IntervalMs;

        /// <summary>Closed bars seen while holding the current position.</summary>
        public long BarsHeld;
    }

    private readonly OnnxPredictor _predictor;
    private readonly ulong _orderQty;
    private readonly float _entryThreshold;
    private readonly float _exitThreshold;
    private readonly long _maxHoldBars;
    private readonly Dictionary<uint, SymbolState> _state = [];
    // Reused per bar: the engine is single-threaded by contract.
    private readonly float[] _features = new float[OnnxPredictor.FeatureCount];
    private readonly double[] _returns = new double[OnnxPredictor.FeatureCount];

    /// <summary>Set on inference failure: cancel everything, never trade again.</summary>
    private bool _poisoned;
    private StrategyDecision? _lastDecision;

    private OnnxStrategy(
        OnnxPredictor predictor, ulong orderQty, float entryThreshold, float exitThreshold,
        long maxHoldBars)
    {
        _predictor = predictor;
        _orderQty = orderQty;
        _entryThreshold = entryThreshold;
        _exitThreshold = exitThreshold;
        _maxHoldBars = maxHoldBars;
    }

    /// <summary>
    /// Load the model and build the strategy. 0 (or negative) parameters
    /// select the defaults; thresholds are bps of probability (6000 = 0.60).
    /// </summary>
    public static OnnxStrategy FromFile(
        string path, ulong orderQty, long entryThresholdBps, long exitThresholdBps,
        long maxHoldBars)
    {
        var entry = entryThresholdBps > 0 ? entryThresholdBps : DefaultEntryThresholdBps;
        var exit = exitThresholdBps > 0 ? exitThresholdBps : DefaultExitThresholdBps;
        if (entry is < 1 or > 10_000 || exit is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                $"onnx thresholds must be within 1..=10000 bps (entry {entry}, exit {exit})");
        }

        OnnxPredictor predictor;
        try
        {
            predictor = OnnxPredictor.Load(path);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"failed to load onnx model {path}: {error.Message}", error);
        }

        return new OnnxStrategy(
            predictor,
            orderQty > 0 ? orderQty : 1,
            entry / 10_000.0f,
            exit / 10_000.0f,
            maxHoldBars > 0 ? maxHoldBars : DefaultMaxHoldBars);
    }

    public StrategyDecision? LastDecision => _lastDecision;

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
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup);
            return;
        }

        float up;
        try
        {
            up = _predictor.Predict(_features).ProbabilityUp;
        }
        catch (Exception)
        {
            _poisoned = true;
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded);
            CancelAll(ctx, output);
            return;
        }
        _lastDecision = new StrategyDecision(up, DecisionStatus.Active);

        if (ctx.Position == 0)
            state.BarsHeld = 0;
        else
            state.BarsHeld++;

        // Working orders at bar close: cancel and decide on a later bar
        // (mirrors mt5-bot's "pending order on instrument" hold).
        if (ctx.OpenOrders.Count > 0)
        {
            CancelAll(ctx, output);
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
        output.Add(new Intent.Place(
            ctx.SymbolId, orderSide, price, (ulong)Math.Abs(delta)));
    }

    private static void CancelAll(in StrategyContext ctx, List<Intent> output)
    {
        foreach (var order in ctx.OpenOrders)
            output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
    }

    /// <summary>
    /// Fold a closed bar into the symbol's return window. Fills
    /// <paramref name="features"/> and returns true once the window is warm;
    /// false while warming up (or after a reset on gaps / bad closes).
    /// </summary>
    private bool UpdateWindow(SymbolState state, in Bar bar, float[] features)
    {
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
        if (state.Closes.Count > OnnxPredictor.FeatureCount + 1)
            state.Closes.RemoveAt(0);
        if (state.Closes.Count < OnnxPredictor.FeatureCount + 1)
            return false;

        for (var i = 0; i < OnnxPredictor.FeatureCount; i++)
            _returns[i] = state.Closes[i + 1].Close / state.Closes[i].Close - 1.0;
        return OnnxPredictor.TryFeaturesFromReturns(_returns, features);
    }

    public void Dispose() => _predictor.Dispose();
}

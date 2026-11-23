namespace Trading.Core.Engine;

/// <summary>Experimental classifier over five consecutive mid-price returns.</summary>
public sealed class TickMlStrategy : IStrategy, IDisposable
{
    private sealed class Window
    {
        public readonly double[] Mids = new double[ModelPredictor.ReturnCount + 1];
        public int Count;
    }

    private readonly IModelPredictor _predictor;
    private readonly Dictionary<uint, Window> _windows = [];
    private readonly float[] _features = new float[ModelPredictor.ReturnCount];
    private readonly ulong _quantity;
    private readonly float _entry;
    private readonly float _exit;
    private bool _failed;

    public bool RequiresAtomicQuotes => true;
    public StrategyDecision? LastDecision { get; private set; }
    public long PredictionCount { get; private set; }
    public string? InferenceFailure { get; private set; }

    public TickMlStrategy(IModelPredictor predictor, ulong quantity, long entryBps, long exitBps)
    {
        if (predictor.FeatureCount != ModelPredictor.ReturnCount
            || predictor.FeatureIndexes is not null || predictor.OutputKind != ModelOutputKind.Probability)
            throw new InvalidOperationException("tick model must be a five-input probability classifier");
        if (quantity is 0 or > long.MaxValue || entryBps is <= 5000 or > 10000
            || exitBps is < 1 or > 5000)
            throw new InvalidOperationException("invalid tick model quantity or probability thresholds");
        _predictor = predictor;
        _quantity = quantity;
        _entry = entryBps / 10_000f;
        _exit = exitBps / 10_000f;
    }

    public static TickMlStrategy FromFile(string path, ulong quantity, long entryBps, long exitBps)
    {
        var predictor = ModelPredictor.Load(path);
        try
        {
            return new TickMlStrategy(predictor, quantity == 0 ? 1 : quantity,
                entryBps == 0 ? 6000 : entryBps, exitBps == 0 ? 5000 : exitBps);
        }
        catch
        {
            predictor.Dispose();
            throw;
        }
    }

    public void OnBook(in StrategyContext ctx, List<Intent> output) { }

    public void OnQuoteGap(in StrategyContext ctx, List<Intent> output)
    {
        _windows.Remove(ctx.SymbolId);
        if (!_failed)
            LastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                "Quote stream interrupted or invalid; model must warm up again.");
        Cancel(ctx, output);
    }

    public void OnTick(in StrategyContext ctx, in QuoteTick tick, List<Intent> output)
    {
        if (_failed)
        {
            Cancel(ctx, output);
            return;
        }
        if (tick.Bid <= 0 || tick.Ask <= tick.Bid)
        {
            OnQuoteGap(ctx, output);
            return;
        }
        if (!_windows.TryGetValue(ctx.SymbolId, out var window))
            _windows[ctx.SymbolId] = window = new Window();
        if (window.Count == window.Mids.Length)
            Array.Copy(window.Mids, 1, window.Mids, 0, window.Mids.Length - 1);
        else
            window.Count++;
        window.Mids[window.Count - 1] = (tick.Bid / 2d) + (tick.Ask / 2d);
        if (window.Count < window.Mids.Length)
        {
            LastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup,
                "Waiting for consecutive valid quotes.");
            return;
        }
        for (var i = 0; i < _features.Length; i++)
        {
            var index = window.Count - 1 - i;
            _features[i] = (float)(window.Mids[index] / window.Mids[index - 1] - 1d);
        }

        float probability;
        try
        {
            probability = _predictor.Predict(_features).ProbabilityUp;
            if (!float.IsFinite(probability) || probability < 0 || probability > 1)
                throw new InvalidOperationException("model returned an invalid probability");
        }
        catch (Exception ex)
        {
            _failed = true;
            InferenceFailure = ex.Message;
            LastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                $"Tick model inference failed; trading disabled: {ex.Message}");
            Console.Error.WriteLine(LastDecision.Value.Reason);
            Cancel(ctx, output);
            return;
        }
        PredictionCount++;
        LastDecision = new StrategyDecision(probability, DecisionStatus.Active,
            "No position change indicated.", SignalKind: "probabilityUp", EntryThreshold: _entry);
        if (ctx.OpenOrders.Count > 0)
        {
            LastDecision = LastDecision.Value with { Reason = "Cancelling working orders before the next decision." };
            Cancel(ctx, output);
            return;
        }
        Side? side = ctx.Position == 0
            ? probability >= _entry ? Side.Buy : probability <= 1 - _entry ? Side.Sell : null
            : ctx.Position > 0 && probability < _exit ? Side.Sell
            : ctx.Position < 0 && probability > 1 - _exit ? Side.Buy : null;
        if (side is { } orderSide)
        {
            LastDecision = LastDecision.Value with
            {
                Reason = ctx.Position == 0 ? orderSide == Side.Buy ? "Long entry signal." : "Short entry signal."
                    : "Exit signal."
            };
            var quantity = ctx.Position == 0 ? _quantity
                : ctx.Position > 0 ? (ulong)ctx.Position : (ulong)(-(Int128)ctx.Position);
            output.Add(new Intent.Place(ctx.SymbolId, orderSide,
                orderSide == Side.Buy ? tick.Ask : tick.Bid, quantity));
        }
    }

    private static void Cancel(in StrategyContext ctx, List<Intent> output)
    {
        foreach (var order in ctx.OpenOrders)
            output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
    }

    public void Dispose() => _predictor.Dispose();
}

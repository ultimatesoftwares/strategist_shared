namespace Trading.Core.Engine;

/// <summary>
/// Cost-aware scalping over tick micro-bars: enter only when the model's
/// predicted payoff clears a multiple of the live, measured round-trip cost,
/// and manage the exit as a layered floating-PnL policy evaluated on every
/// tick, marked at the exit side of the book (bid for longs, ask for shorts).
///
/// <para><b>Model contract</b>: the same named-feature edge regression as
/// <see cref="TickEdgeStrategy"/> — <c>Score</c> is the predicted payoff in
/// bps of fading the Z60 stretch. This strategy uses the score's sign:
/// a strongly positive score is a mean-reversion (fade) candidate, a strongly
/// negative one a momentum candidate (fading predicted to lose ⇒ go with the
/// stretch), chosen by the efficiency-ratio regime gate.</para>
///
/// <para><b>Cost model</b> (bps of mid, per symbol, measured live):
/// round-trip cost <c>C = spreadNow + 2·commission + 2·slipEwma</c>, where
/// <c>spreadNow</c> comes from the decision tick, commission is a manifest
/// constant (venues do not report it) and <c>slipEwma</c> is learned from the
/// strategy's own fills (fill price vs the intended touch).</para>
///
/// <para><b>Entry</b> (once per closed micro-bar, while flat) requires every
/// gate in order — the first failure becomes the published decision reason:
/// warm window → clean book → cooldowns → budgets (trades/min, consecutive
/// losses, daily loss) → spread sanity (live vs rolling median) → volatility
/// floor/ceiling → symbol economics (typical target must clear typical cost;
/// the live ETHUSD lesson) → signal (|Z60| ≥ entryZ and |Score| ≥ k·C with a
/// regime-consistent sign) → stop feasibility → sizing against the venue's
/// min/step/max. Entries are marketable IOC at the touch; one position per
/// symbol; opposite-side intents never fire while holding (no hedging).</para>
///
/// <para><b>Exit</b> (every tick, priority order): ATR-scaled stop (floored
/// above cost), dynamic take profit (max of cost-covering floor and
/// tpAtrMult·sigma, decaying with age so stale trades close at small positive
/// PnL), trailing profit lock (give back at most trailGiveback of peak net),
/// break-even ratchet, bar-close edge invalidation, and the hard max hold.
/// An exit never waits for the next bar close behind a cancel: the moment the
/// cancel (or reject) of a working order confirms, the exit order is placed
/// from <see cref="OnExecution"/>. A venue-rejected exit retries on the next
/// tick while the position persists — an exit can never be starved by order
/// state.</para>
///
/// <para><b>Self-protection</b>: a streak of venue-rejected entries disables
/// the symbol for the session with a surfaced reason (unknown venue minimums
/// — post-mortem §12.4); daily budgets halt entries but never exits; an
/// inference failure poisons the strategy (cancel everything, never trade
/// again), matching <see cref="TickEdgeStrategy"/>.</para>
/// </summary>
public sealed class CostScalperStrategy : IStrategy, IWarmStateSource, IDisposable
{
    // ── Manifest-tunable defaults (0 in the manifest selects these) ──
    public const double DefaultCostMultiple = 1.75;
    public const double DefaultTrendEr = 0.35;
    public const double DefaultSpreadCapRatio = 1.5;
    public const double DefaultVolFloorBps = 0.5;
    public const double DefaultVolCeilRatio = 3.0;
    public const double DefaultTpAtrMult = 2.0;
    public const double DefaultCostFloorMult = 1.5;
    public const double DefaultStopAtrMult = 3.0;
    public const double DefaultTrailGiveback = 0.4;
    public const double DefaultDecayRate = 0.8;
    public const long DefaultCooldownBars = 5;
    public const long DefaultLossCooldownBars = 15;
    public const long DefaultMaxTradesPerMin = 6;
    public const long DefaultMaxConsecutiveLosses = 5;
    public const double DefaultMaxDailyLossBps = 500;
    public const long DefaultMaxHoldBars = 240;

    // ── Code constants (deliberately not tunable: fewer knobs to overfit) ──
    /// <summary>Peak net PnL (fraction of target) that arms the trailing lock.</summary>
    public const double TrailArmFraction = 0.8;
    /// <summary>Peak net PnL (fraction of target) that arms the break-even ratchet.</summary>
    public const double BreakEvenArmFraction = 0.5;
    /// <summary>Net PnL at/below which an armed break-even ratchet closes, bps.</summary>
    public const double BreakEvenBufferBps = 0.0;
    /// <summary>The decayed target never shrinks below this, bps.</summary>
    public const double StaleFloorBps = 0.5;
    /// <summary>Hard lower bound for the stop distance, bps.</summary>
    public const double MinStopBps = 4.0;
    /// <summary>Hard upper bound for the stop distance, bps. A symbol whose
    /// cost floor exceeds it is uneconomic and entry is vetoed.</summary>
    public const double MaxStopBps = 50.0;
    /// <summary>The stop must sit at least this multiple of round-trip cost away.</summary>
    public const double StopCostFloorMult = 1.5;
    /// <summary>Rolling spread-median window (one sample per closed micro-bar).</summary>
    public const int SpreadWindow = 256;
    /// <summary>Spread samples required before the spread/economics gates engage.</summary>
    public const int MinSpreadSamples = 30;
    /// <summary>EWMA weight for realized slippage samples.</summary>
    public const double SlipAlpha = 0.1;
    /// <summary>EWMA weight for the slow "typical sigma" economics estimate.</summary>
    public const double SigmaTypicalAlpha = 0.02;
    /// <summary>Consecutive venue-rejected entries before the symbol self-disables.</summary>
    public const int MaxEntryRejects = 3;
    /// <summary>Bars behind the short-horizon volatility (sigma) estimate.</summary>
    public const int SigmaWindow = 30;
    /// <summary>Bars behind the efficiency-ratio regime estimate.</summary>
    public const int ErWindow = 60;

    private sealed class Trade
    {
        public int Direction;            // +1 long, -1 short
        public long EntryPrice;          // scaled; intended touch until a fill price arrives
        public bool EntryPriceFromFill;
        public bool AnyFill;
        public double EntryCostBps;      // frozen round-trip cost C at entry
        public double StopBps;           // frozen at entry
        public double Target0Bps;        // frozen at entry
        public long AgeBars;
        public double PeakNetBps = double.NegativeInfinity;
        public bool TrailArmed;
        public bool BreakEvenArmed;
        public string? ExitReason;       // set when an exit layer fires
    }

    private sealed class PendingOrder
    {
        public Side Side;
        public long IntendedPrice;       // the touch at decision time, scaled
        public bool IsExit;
    }

    private sealed class SymbolState
    {
        public BarFeatureWindow Window = new();
        public long BucketStartMs = long.MinValue;
        public double LastMid;
        public ulong TickCount;
        public long LastBid;
        public long LastAsk;
        /// <summary>Fill-accounted position mirror so <see cref="OnExecution"/>
        /// (which has no context) can size exits; re-synced to the engine's
        /// position on every tick.</summary>
        public long PositionMirror;
        /// <summary>Closed micro-bars seen (monotonic; survives window resets
        /// so cooldowns stay meaningful across quote gaps).</summary>
        public long BarIndex;

        public readonly double[] SpreadSamples = new double[SpreadWindow];
        public int SpreadCount;
        public int SpreadNext;

        public double SlipEwmaBps;
        public double SigmaTypicalBps = double.NaN;
        public double LastSigmaBps = double.NaN;

        public SymbolTradeSpec Spec;
        public Trade? Open;
        public PendingOrder? Pending;
        /// <summary>An exit fired; stays set until the position is flat.</summary>
        public bool WantFlat;
        /// <summary>The venue refused an exit; retry on the next tick.</summary>
        public bool ExitRetry;

        public long LastExitBar = long.MinValue;
        public long LastLossExitBar = long.MinValue;
        public long EntryMinute = long.MinValue;
        public int EntriesThisMinute;
        public int ConsecutiveLosses;
        public double DayRealizedBps;
        public long UtcDay = long.MinValue;
        public int EntryRejectStreak;
        /// <summary>Session-sticky self-disable reason (venue reject streak).</summary>
        public string? DisabledReason;

        public double SpreadMedianBps()
        {
            if (SpreadCount < MinSpreadSamples)
                return double.NaN;
            Span<double> sorted = stackalloc double[SpreadCount];
            SpreadSamples.AsSpan(0, SpreadCount).CopyTo(sorted);
            sorted.Sort();
            return sorted[SpreadCount / 2];
        }

        public void AddSpreadSample(double spreadBps)
        {
            SpreadSamples[SpreadNext] = spreadBps;
            SpreadNext = (SpreadNext + 1) % SpreadWindow;
            if (SpreadCount < SpreadWindow)
                SpreadCount++;
        }
    }

    private readonly IModelPredictor _predictor;
    private readonly ulong _orderQty;
    private readonly long _maxHoldBars;
    private readonly double _entryZ;
    private readonly double _costMultiple;
    private readonly double _commissionBps;
    private readonly double _trendEr;
    private readonly double _spreadCapRatio;
    private readonly double _volFloorBps;
    private readonly double _volCeilRatio;
    private readonly double _tpAtrMult;
    private readonly double _costFloorMult;
    private readonly double _stopAtrMult;
    private readonly double _trailGiveback;
    private readonly double _decayRate;
    private readonly long _cooldownBars;
    private readonly long _lossCooldownBars;
    private readonly long _maxTradesPerMin;
    private readonly long _maxConsecutiveLosses;
    private readonly double _maxDailyLossBps;

    private readonly IReadOnlyList<int> _featureIndexes;
    private readonly Dictionary<uint, SymbolState> _state = [];
    private readonly Dictionary<uint, List<(double Close, double Volume)>> _pendingWarm = [];
    // Reused per event: the engine is single-threaded by contract.
    private readonly float[] _features;
    private readonly double[] _barFeatures = new double[BarFeatures.Names.Count];

    private bool _poisoned;
    private StrategyDecision? _lastDecision;

    /// <summary>
    /// Build the strategy over an already-loaded predictor. 0/NaN tuning
    /// selects the documented default; see <see cref="FromFile"/> for the
    /// manifest-driven entry point.
    /// </summary>
    public CostScalperStrategy(
        IModelPredictor predictor, ulong orderQty, long maxHoldBars, long barMs,
        double entryZ, CostScalperSettings? settings = null)
    {
        if (predictor.OutputKind != ModelOutputKind.Edge || predictor.FeatureIndexes is null)
            throw new InvalidOperationException(
                "costScalper requires a named-feature regression (edge) model over BarFeatures");
        if (barMs is < TickEdgeStrategy.MinBarMs or > TickEdgeStrategy.MaxBarMs)
            throw new InvalidOperationException(
                $"costScalper micro-bar length must be within {TickEdgeStrategy.MinBarMs}..={TickEdgeStrategy.MaxBarMs} ms (got {barMs})");
        if (orderQty is 0 or > long.MaxValue)
            throw new InvalidOperationException("costScalper requires a positive order qty");
        if (!double.IsFinite(entryZ) || entryZ is <= 0 or > MlStrategy.MaxZGate)
            throw new InvalidOperationException(
                $"costScalper entryZ must be within 0..={MlStrategy.MaxZGate} (got {entryZ})");

        var s = settings ?? new CostScalperSettings();
        static double Pick(double value, double fallback) => value > 0 ? value : fallback;
        static long Pick64(long value, long fallback) => value > 0 ? value : fallback;
        _predictor = predictor;
        _orderQty = orderQty;
        _maxHoldBars = Pick64(maxHoldBars, DefaultMaxHoldBars);
        _entryZ = entryZ;
        _costMultiple = Pick(s.CostMultiple, DefaultCostMultiple);
        _commissionBps = Math.Max(0, s.CommissionBps);
        _trendEr = Pick(s.TrendEr, DefaultTrendEr);
        _spreadCapRatio = Pick(s.SpreadCapRatio, DefaultSpreadCapRatio);
        _volFloorBps = Pick(s.VolFloorBps, DefaultVolFloorBps);
        _volCeilRatio = Pick(s.VolCeilRatio, DefaultVolCeilRatio);
        _tpAtrMult = Pick(s.TpAtrMult, DefaultTpAtrMult);
        _costFloorMult = Pick(s.CostFloorMult, DefaultCostFloorMult);
        _stopAtrMult = Pick(s.StopAtrMult, DefaultStopAtrMult);
        _trailGiveback = Pick(s.TrailGiveback, DefaultTrailGiveback);
        _decayRate = Pick(s.DecayRate, DefaultDecayRate);
        _cooldownBars = Pick64(s.CooldownBars, DefaultCooldownBars);
        _lossCooldownBars = Pick64(s.LossCooldownBars, DefaultLossCooldownBars);
        _maxTradesPerMin = Pick64(s.MaxTradesPerMin, DefaultMaxTradesPerMin);
        _maxConsecutiveLosses = Pick64(s.MaxConsecutiveLosses, DefaultMaxConsecutiveLosses);
        _maxDailyLossBps = Pick(s.MaxDailyLossBps, DefaultMaxDailyLossBps);
        if (_costFloorMult <= 1)
            throw new InvalidOperationException(
                "costScalper costFloorMult must be > 1: the take profit must clear the round-trip cost");
        BarMs = barMs;
        _featureIndexes = predictor.FeatureIndexes;
        _features = new float[predictor.FeatureCount];
    }

    /// <summary>Load the model and build the strategy (0 = default tuning).</summary>
    public static CostScalperStrategy FromFile(
        string path, ulong orderQty, long maxHoldBars, long barMs, double entryZ,
        CostScalperSettings? settings = null)
    {
        IModelPredictor predictor;
        try
        {
            predictor = ModelPredictor.Load(path);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"failed to load model {path}: {error.Message}", error);
        }
        try
        {
            return new CostScalperStrategy(
                predictor,
                orderQty > 0 ? orderQty : 1,
                maxHoldBars,
                barMs > 0 ? barMs : TickEdgeStrategy.DefaultBarMs,
                entryZ > 0 ? entryZ : MlStrategy.DefaultEdgeEntryZ,
                settings);
        }
        catch
        {
            predictor.Dispose();
            throw;
        }
    }

    /// <summary>Micro-bar length in milliseconds.</summary>
    public long BarMs { get; }

    public double CostMultiple => _costMultiple;
    public double CommissionBps => _commissionBps;
    public double EntryZ => _entryZ;
    public long MaxHoldBars => _maxHoldBars;

    public bool RequiresAtomicQuotes => true;

    /// <summary>Same reasoning as <see cref="TickEdgeStrategy.QuoteGapTolerance"/>:
    /// sparse change-only feeds pause legitimately; only a silence long enough
    /// to suggest a dead-but-undetected feed invalidates the window.</summary>
    public TimeSpan QuoteGapTolerance => TimeSpan.FromMilliseconds(Math.Max(30 * BarMs, 60_000));

    public StrategyDecision? LastDecision => _lastDecision;

    public int WarmupBars => BarFeatureWindow.WarmTarget;

    public long WarmupBarIntervalMs => BarMs;

    /// <summary>Queue a history bar; rebased onto the first live mid exactly
    /// like <see cref="TickEdgeStrategy.WarmBar"/> (log features are
    /// shift-invariant, so cross-venue/checkpointed levels are harmless).</summary>
    public void WarmBar(in Bar bar)
    {
        if (_poisoned || bar.Close <= 0)
            return;
        if (!_pendingWarm.TryGetValue(bar.SymbolId, out var pending))
            _pendingWarm[bar.SymbolId] = pending = new(BarFeatureWindow.WarmTarget);
        pending.Add((bar.Close, bar.Volume));
        if (pending.Count > BarFeatureWindow.WarmTarget)
            pending.RemoveAt(0);
    }

    public void SetSymbolSpec(uint symbolId, in SymbolTradeSpec spec) =>
        GetState(symbolId).Spec = spec;

    public int ExportWarmBars(uint symbolId, List<WarmupBarSnapshot> into) =>
        _state.TryGetValue(symbolId, out var state) ? state.Window.ExportBars(BarMs, into) : 0;

    /// <summary>Tick-driven: book deltas carry no extra signal.</summary>
    public void OnBook(in StrategyContext ctx, List<Intent> output)
    {
    }

    public void OnQuoteGap(in StrategyContext ctx, List<Intent> output)
    {
        // Rebuild the micro-bars, but keep the trade state (the stop, target
        // and max hold keep protecting the position while the window re-warms)
        // and the learned cost statistics (they describe the venue, not the
        // window).
        if (_state.TryGetValue(ctx.SymbolId, out var state))
        {
            state.Window = new BarFeatureWindow();
            state.BucketStartMs = long.MinValue;
            state.LastMid = 0;
            state.TickCount = 0;
        }
        if (!_poisoned)
        {
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded,
                "Quote stream interrupted or invalid; micro-bars must warm up again.",
                WarmupCount: 0, WarmupTarget: BarFeatureWindow.WarmTarget);
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

        var state = GetState(ctx.SymbolId);
        state.PositionMirror = ctx.Position;
        var mid = (tick.Bid / 2.0) + (tick.Ask / 2.0);
        var tsMs = (long)(tick.TsNanos / 1_000_000UL);
        var bucket = tsMs - tsMs % BarMs;

        // Seeded history folds in on the symbol's first live tick (see WarmBar).
        if (_pendingWarm.Count > 0 && _pendingWarm.Remove(ctx.SymbolId, out var pending)
            && state.Window.Count == 0 && pending.Count > 0)
        {
            var factor = mid / pending[^1].Close;
            for (var k = 0; k < pending.Count; k++)
            {
                state.Window.Add(
                    bucket - (pending.Count - k) * BarMs,
                    pending[k].Close * factor, pending[k].Volume);
            }
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup,
                "Seeded with bar history; live micro-bars resume the window.",
                WarmupCount: state.Window.Count, WarmupTarget: BarFeatureWindow.WarmTarget);
        }

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
        state.LastBid = tick.Bid;
        state.LastAsk = tick.Ask;

        if (output.Count == 0)
            ManagePosition(ctx, tick, state, output);
    }

    public void OnExecution(in ExecutionReport report, List<Intent> output)
    {
        if (_poisoned || !_state.TryGetValue(report.SymbolId, out var state))
            return;

        if (report.Status is OrderStatus.PartiallyFilled or OrderStatus.Filled
            && report.FillQty > 0)
        {
            state.PositionMirror += report.Side == Side.Buy
                ? (long)report.FillQty : -(long)report.FillQty;
            NoteFill(state, report);
        }

        var pending = state.Pending;
        switch (report.Status)
        {
            case OrderStatus.Accepted:
            case OrderStatus.PartiallyFilled:
                return;

            case OrderStatus.Filled:
                state.Pending = null;
                if (pending is { IsExit: false })
                    state.EntryRejectStreak = 0;
                if (pending is { IsExit: true } && state.PositionMirror == 0)
                    FinalizeClosedTrade(state, report);
                else if (state.WantFlat || state.ExitRetry)
                    TryExitFromExecution(state, report.SymbolId, output);
                break;

            case OrderStatus.Cancelled:
                state.Pending = null;
                if (pending is { IsExit: false } && state.Open is { AnyFill: false })
                    state.Open = null; // The entry never filled; nothing to manage.
                if (state.WantFlat || state.ExitRetry)
                    TryExitFromExecution(state, report.SymbolId, output);
                break;

            case OrderStatus.Rejected:
                state.Pending = null;
                if (pending is { IsExit: true })
                {
                    // The venue refused the exit (e.g. a 35=j business
                    // reject). The position still exists: retry on the next
                    // tick, forever — an exit is never abandoned.
                    state.ExitRetry = true;
                    _lastDecision = new StrategyDecision(
                        _lastDecision?.Forecast ?? double.NaN, DecisionStatus.Active,
                        "Venue rejected the exit order; retrying on the next tick.",
                        SignalKind: "edgeBps");
                }
                else if (pending is { IsExit: false })
                {
                    if (state.Open is { AnyFill: false })
                        state.Open = null;
                    state.EntryRejectStreak++;
                    if (state.EntryRejectStreak >= MaxEntryRejects && state.DisabledReason is null)
                    {
                        state.DisabledReason =
                            $"sizing: venue refused {state.EntryRejectStreak} consecutive entries "
                            + "(likely a volume minimum/step mismatch); symbol disabled for this "
                            + "session — enrich the instrument catalog min/step/max";
                        _lastDecision = new StrategyDecision(
                            double.NaN, DecisionStatus.Degraded, state.DisabledReason);
                    }
                }
                if (state.WantFlat && state.PositionMirror != 0)
                    TryExitFromExecution(state, report.SymbolId, output);
                break;
        }
    }

    public void Dispose() => _predictor.Dispose();

    // ───────────────────────── internals ─────────────────────────

    private SymbolState GetState(uint symbolId)
    {
        if (!_state.TryGetValue(symbolId, out var state))
            _state[symbolId] = state = new SymbolState();
        return state;
    }

    private static double SpreadBps(long bid, long ask)
    {
        var mid = (bid / 2.0) + (ask / 2.0);
        return mid > 0 ? (ask - bid) / mid * 1e4 : double.NaN;
    }

    /// <summary>Live round-trip cost C in bps: spread once + commission and slippage per leg.</summary>
    private double RoundTripCostBps(SymbolState state, double spreadNowBps) =>
        spreadNowBps + 2 * _commissionBps + 2 * state.SlipEwmaBps;

    /// <summary>
    /// The direction the signal supports under the regime gate, or 0.
    /// Fade (mean reversion) when the tape is choppy and fading is predicted
    /// to pay ≥ k·C; momentum when the tape trends and fading is predicted to
    /// LOSE ≥ k·C (so going with the stretch is predicted to pay).
    /// </summary>
    private int CandidateDirection(double score, double z, double er, double costBps)
    {
        if (Math.Abs(z) < _entryZ || !double.IsFinite(er))
            return 0;
        var need = _costMultiple * costBps;
        var fade = z > 0 ? -1 : 1;
        if (er < _trendEr && score >= need)
            return fade;
        if (er >= _trendEr && score <= -need)
            return -fade;
        return 0;
    }

    /// <summary>Fold the finished micro-bar into the window, then run the
    /// bar-cadence duties: budgets, edge invalidation, and the entry gates.</summary>
    private void OnMicroBarClose(
        in StrategyContext ctx, in QuoteTick tick, SymbolState state, List<Intent> output)
    {
        var warm = state.Window.Add(state.BucketStartMs, state.LastMid, state.TickCount)
            && state.Window.IsWarm;
        state.BarIndex++;
        if (state.Open is { } aging && ctx.Position != 0)
            aging.AgeBars++;

        var spreadNow = SpreadBps(tick.Bid, tick.Ask);
        if (double.IsFinite(spreadNow) && spreadNow >= 0)
            state.AddSpreadSample(spreadNow);

        // UTC day rollover resets the daily budgets.
        var day = state.BucketStartMs / 86_400_000;
        if (day != state.UtcDay)
        {
            state.UtcDay = day;
            state.DayRealizedBps = 0;
            state.ConsecutiveLosses = 0;
        }

        if (!warm || !ComputeFeatures(state))
        {
            _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Warmup,
                "Waiting for sufficient micro-bars, or ignoring a stale bucket.",
                WarmupCount: state.Window.Count, WarmupTarget: BarFeatureWindow.WarmTarget);
            return; // Tick-level exits (stop/target/max hold) keep protecting any position.
        }

        var sigma = state.Window.SigmaBps(SigmaWindow);
        state.LastSigmaBps = sigma;
        if (double.IsFinite(sigma))
        {
            state.SigmaTypicalBps = double.IsNaN(state.SigmaTypicalBps)
                ? sigma
                : state.SigmaTypicalBps + SigmaTypicalAlpha * (sigma - state.SigmaTypicalBps);
        }

        ModelPrediction prediction;
        try
        {
            prediction = _predictor.Predict(_features);
        }
        catch (Exception)
        {
            Poison(ctx, "Model inference failed; trading is disabled.", output);
            return;
        }
        if (!float.IsFinite(prediction.Score))
        {
            Poison(ctx, "Model returned a non-finite score; trading is disabled.", output);
            return;
        }

        var score = (double)prediction.Score;
        var z = _barFeatures[BarFeatures.Z60];
        var er = state.Window.EfficiencyRatio(ErWindow);
        var cost = RoundTripCostBps(state, spreadNow);

        if (ctx.Position != 0)
        {
            // Bar-close exit layer: edge invalidation. The model now backs a
            // paid move against the position.
            var trade = state.Open;
            var candidate = CandidateDirection(score, z, er, cost);
            if (trade is not null && candidate != 0 && candidate == -trade.Direction)
            {
                trade.ExitReason = "Edge invalidated: the model backs the opposite direction";
                state.WantFlat = true;
            }
            _lastDecision = new StrategyDecision(score, DecisionStatus.Active,
                state.WantFlat
                    ? $"{trade?.ExitReason ?? "Exit pending"}; flattening."
                    : "Holding; exits are managed per tick.",
                SignalKind: "edgeBps", Z60: z,
                EntryThreshold: _costMultiple * cost, ZThreshold: _entryZ);
            // The tick path placed after this bar close executes the exit.
            return;
        }

        if (state.WantFlat)
        {
            state.WantFlat = false; // Already flat.
            state.ExitRetry = false;
        }

        // ── Entry gate chain. The first failing gate is the published reason. ──
        var veto = EntryVeto(ctx, state, spreadNow, sigma, cost);
        if (veto is not null)
        {
            _lastDecision = new StrategyDecision(score, DecisionStatus.Active, veto,
                SignalKind: "edgeBps", Z60: z,
                EntryThreshold: _costMultiple * cost, ZThreshold: _entryZ);
            if (ctx.OpenOrders.Count > 0 && state.Pending is null)
                CancelAll(ctx, output); // Stale working orders we no longer track.
            return;
        }

        var direction = CandidateDirection(score, z, er, cost);
        if (direction == 0)
        {
            var need = _costMultiple * cost;
            var regime = er < _trendEr ? "fade" : "momentum";
            _lastDecision = new StrategyDecision(score, DecisionStatus.Active,
                Math.Abs(z) < _entryZ
                    ? $"No entry: |Z60| {Math.Abs(z):0.00} < {_entryZ:0.00}."
                    : $"No entry ({regime} regime, ER {er:0.00}): |score| {Math.Abs(score):0.0} bps "
                      + $"does not clear k·C = {need:0.0} bps (C {cost:0.0}).",
                SignalKind: "edgeBps", Z60: z, EntryThreshold: need, ZThreshold: _entryZ);
            return;
        }

        // Stop feasibility: the cost floor must fit under the stop ceiling.
        var stopFloor = Math.Max(MinStopBps, StopCostFloorMult * cost);
        if (stopFloor > MaxStopBps)
        {
            _lastDecision = new StrategyDecision(score, DecisionStatus.Active,
                $"No entry: round-trip cost {cost:0.0} bps needs a stop beyond {MaxStopBps} bps.",
                SignalKind: "edgeBps", Z60: z,
                EntryThreshold: _costMultiple * cost, ZThreshold: _entryZ);
            return;
        }

        var qty = SizedQty(state, out var sizingVeto);
        if (qty == 0)
        {
            _lastDecision = new StrategyDecision(score, DecisionStatus.Active, sizingVeto,
                SignalKind: "edgeBps", Z60: z,
                EntryThreshold: _costMultiple * cost, ZThreshold: _entryZ);
            return;
        }

        // ── Enter. ──
        var stop = Math.Clamp(
            _stopAtrMult * (double.IsFinite(sigma) ? sigma : 0), stopFloor, MaxStopBps);
        var target0 = Math.Max(_costFloorMult * cost, _tpAtrMult * (double.IsFinite(sigma) ? sigma : 0));
        var side = direction > 0 ? Side.Buy : Side.Sell;
        var price = side == Side.Buy ? tick.Ask : tick.Bid;
        if (price <= 0)
            return;

        var minute = state.BucketStartMs / 60_000;
        if (minute != state.EntryMinute)
        {
            state.EntryMinute = minute;
            state.EntriesThisMinute = 0;
        }
        state.EntriesThisMinute++;

        state.Open = new Trade
        {
            Direction = direction,
            EntryPrice = price,
            EntryCostBps = cost,
            StopBps = stop,
            Target0Bps = target0,
        };
        state.Pending = new PendingOrder { Side = side, IntendedPrice = price, IsExit = false };
        var regimeName = er < _trendEr ? "fade" : "momentum";
        _lastDecision = new StrategyDecision(score, DecisionStatus.Active,
            $"Entry ({regimeName}): {(direction > 0 ? "long" : "short")} {qty}; "
            + $"|score| {Math.Abs(score):0.0} ≥ k·C {_costMultiple * cost:0.0} bps "
            + $"(C {cost:0.0}); target {target0:0.0}, stop {stop:0.0} bps.",
            SignalKind: "edgeBps", Z60: z,
            EntryThreshold: _costMultiple * cost, ZThreshold: _entryZ);
        output.Add(new Intent.Place(ctx.SymbolId, side, price, qty));
    }

    /// <summary>The first failing pre-signal entry gate, or null. Order:
    /// disabled → clean book → cooldowns → budgets → spread sanity →
    /// volatility floor/ceiling → symbol economics.</summary>
    private string? EntryVeto(
        in StrategyContext ctx, SymbolState state, double spreadNow, double sigma, double cost)
    {
        if (state.DisabledReason is not null)
            return state.DisabledReason;
        if (ctx.OpenOrders.Count > 0 || state.Pending is not null)
            return "Waiting: working orders must resolve before a new entry.";

        if (state.LastExitBar != long.MinValue
            && state.BarIndex - state.LastExitBar < _cooldownBars)
            return $"Cooldown: {state.BarIndex - state.LastExitBar}/{_cooldownBars} bars since the last exit.";
        if (state.LastLossExitBar != long.MinValue
            && state.BarIndex - state.LastLossExitBar < _lossCooldownBars)
            return $"Loss cooldown: {state.BarIndex - state.LastLossExitBar}/{_lossCooldownBars} bars since the last losing exit.";

        var minute = state.BucketStartMs / 60_000;
        if (minute == state.EntryMinute && state.EntriesThisMinute >= _maxTradesPerMin)
            return $"Budget: {state.EntriesThisMinute} entries this minute reached the {_maxTradesPerMin}/min cap.";
        if (state.ConsecutiveLosses >= _maxConsecutiveLosses)
            return $"Budget: {state.ConsecutiveLosses} consecutive losses; entries halted until the UTC day rolls over.";
        if (state.DayRealizedBps <= -_maxDailyLossBps)
            return $"Budget: daily realized {state.DayRealizedBps:0.0} bps breached -{_maxDailyLossBps:0.0}; entries halted for the day.";

        var median = state.SpreadMedianBps();
        if (!double.IsFinite(median))
            return $"Measuring costs: {state.SpreadCount}/{MinSpreadSamples} spread samples.";
        if (spreadNow > _spreadCapRatio * median)
            return $"Spread gate: live {spreadNow:0.0} bps > {_spreadCapRatio:0.0}× median {median:0.0} bps.";

        if (!double.IsFinite(sigma) || sigma < _volFloorBps)
            return $"Volatility floor: sigma {(double.IsFinite(sigma) ? sigma : 0):0.00} bps < {_volFloorBps:0.00} bps (dead market cannot pay costs).";
        // Ceiling vs the slow typical-sigma EWMA, not the VolRatio feature:
        // std30/std240 over one series is bounded by √(240/30) ≈ 2.83, so a
        // ratio gate above that could never fire.
        if (double.IsFinite(state.SigmaTypicalBps) && state.SigmaTypicalBps > 0
            && sigma > _volCeilRatio * state.SigmaTypicalBps)
            return $"Volatility ceiling: sigma {sigma:0.00} bps > {_volCeilRatio:0.0}× typical {state.SigmaTypicalBps:0.00} (no entries into a spike).";

        var typicalTarget = _tpAtrMult * state.SigmaTypicalBps;
        var typicalCost = _costFloorMult * (median + 2 * _commissionBps);
        if (!double.IsFinite(state.SigmaTypicalBps) || typicalTarget < typicalCost)
            return $"Economics: typical target {typicalTarget:0.0} bps cannot clear cost floor {typicalCost:0.0} bps; symbol skipped.";

        return null;
    }

    /// <summary>Configured qty snapped down to the venue step and clamped to
    /// the maximum; 0 (with a veto reason) when the minimum cannot be met.</summary>
    private ulong SizedQty(SymbolState state, out string? veto)
    {
        var spec = state.Spec;
        var qty = _orderQty;
        if (spec.MaxQty > 0 && qty > spec.MaxQty)
            qty = spec.MaxQty;
        if (spec.QtyStep > 1)
            qty -= qty % spec.QtyStep;
        if (qty == 0 || (spec.MinQty > 0 && qty < spec.MinQty))
        {
            veto = $"Sizing: venue minimum {spec.MinQty} / step {spec.QtyStep} engine lots "
                + $"cannot be met from the configured qty {_orderQty}; entry vetoed.";
            return 0;
        }
        veto = null;
        return qty;
    }

    /// <summary>Tick-cadence exit engine: mark the position at the exit side
    /// of the book, update the peak/ratchets, and run the exit layers.</summary>
    private void ManagePosition(
        in StrategyContext ctx, in QuoteTick tick, SymbolState state, List<Intent> output)
    {
        if (ctx.Position == 0)
        {
            if (state.Open is { AnyFill: true } && state.Pending is null && ctx.OpenOrders.Count == 0)
            {
                // Closed outside our order flow (manual close, stop-out, or a
                // fill we finalized in OnExecution already): settle at the mark.
                FinalizeExternallyClosedTrade(state, tick);
            }
            return;
        }

        var trade = state.Open ?? AdoptExternalPosition(ctx, tick, state);
        var exitPx = trade.Direction > 0 ? tick.Bid : tick.Ask;
        if (exitPx <= 0 || trade.EntryPrice <= 0)
            return;

        var gross = trade.Direction * Math.Log(exitPx / (double)trade.EntryPrice) * 1e4;
        var net = gross - 2 * _commissionBps - state.SlipEwmaBps;
        if (net > trade.PeakNetBps)
            trade.PeakNetBps = net;
        if (!trade.TrailArmed && trade.PeakNetBps >= TrailArmFraction * trade.Target0Bps)
            trade.TrailArmed = true;
        if (!trade.BreakEvenArmed && trade.PeakNetBps >= BreakEvenArmFraction * trade.Target0Bps)
            trade.BreakEvenArmed = true;

        var reason = state.ExitRetry
            ? trade.ExitReason ?? "Retrying the exit after a venue reject"
            : ExitLayer(trade, gross, net);
        if (reason is null && !state.WantFlat)
            return;
        trade.ExitReason ??= reason;

        if (ctx.OpenOrders.Count > 0)
        {
            // Exits pre-empt whatever is working: cancel now; the exit order
            // is placed the moment the cancel outcome arrives (OnExecution),
            // not on the next bar close.
            if (!state.WantFlat)
            {
                state.WantFlat = true;
                _lastDecision = new StrategyDecision(
                    _lastDecision?.Forecast ?? double.NaN, DecisionStatus.Active,
                    $"{trade.ExitReason ?? reason}; cancelling working orders to flatten.",
                    SignalKind: "edgeBps");
                CancelAll(ctx, output);
            }
            return;
        }
        if (state.Pending is not null)
            return; // One order in flight at a time; its outcome resolves next.

        state.WantFlat = true;
        PlaceExit(ctx.SymbolId, ctx.Position, tick.Bid, tick.Ask, state,
            trade.ExitReason ?? reason ?? "Flattening", output);
    }

    /// <summary>The highest-priority exit layer that fires, or null.</summary>
    private string? ExitLayer(Trade trade, double grossBps, double netBps)
    {
        if (grossBps <= -trade.StopBps)
            return $"Stop loss: {grossBps:0.0} bps ≤ -{trade.StopBps:0.0}";

        var target = Math.Max(
            StaleFloorBps,
            trade.Target0Bps * (1 - _decayRate * trade.AgeBars / (double)_maxHoldBars));
        if (netBps >= target)
            return $"Take profit: net {netBps:0.0} bps ≥ target {target:0.0} (age {trade.AgeBars} bars)";

        if (trade.TrailArmed && netBps <= trade.PeakNetBps * (1 - _trailGiveback))
            return $"Profit lock: net {netBps:0.0} bps gave back {_trailGiveback:P0} of peak {trade.PeakNetBps:0.0}";

        if (trade.BreakEvenArmed && netBps <= BreakEvenBufferBps)
            return $"Break-even stop: net {netBps:0.0} bps after reaching {BreakEvenArmFraction:P0} of target";

        if (trade.AgeBars >= _maxHoldBars)
            return $"Maximum hold: {trade.AgeBars} bars";

        return null;
    }

    private void PlaceExit(
        uint symbolId, long position, long bid, long ask, SymbolState state,
        string reason, List<Intent> output)
    {
        var side = position > 0 ? Side.Sell : Side.Buy;
        var price = side == Side.Buy ? ask : bid;
        if (price <= 0 || position == 0)
            return;
        state.Pending = new PendingOrder { Side = side, IntendedPrice = price, IsExit = true };
        state.ExitRetry = false;
        _lastDecision = new StrategyDecision(
            _lastDecision?.Forecast ?? double.NaN, DecisionStatus.Active,
            $"{reason}; flattening.", SignalKind: "edgeBps");
        output.Add(new Intent.Place(symbolId, side, price, (ulong)Math.Abs(position)));
    }

    /// <summary>Place the pending exit as soon as a cancel/reject outcome
    /// frees the book — never wait for the next bar close (§12.1).</summary>
    private void TryExitFromExecution(SymbolState state, uint symbolId, List<Intent> output)
    {
        if (state.Pending is not null || state.PositionMirror == 0
            || state.LastBid <= 0 || state.LastAsk <= 0)
            return;
        PlaceExit(symbolId, state.PositionMirror, state.LastBid, state.LastAsk, state,
            state.Open?.ExitReason ?? "Flattening after order-state resolution", output);
    }

    /// <summary>Correct the entry price to the actual fill and learn slippage
    /// from the fill-vs-intended difference (floored at zero: price
    /// improvement must not shrink the cost estimate).</summary>
    private void NoteFill(SymbolState state, in ExecutionReport report)
    {
        var pending = state.Pending;
        if (pending is null || report.FillPrice <= 0 || pending.IntendedPrice <= 0)
        {
            if (state.Open is { } opened && pending is { IsExit: false })
                opened.AnyFill = true;
            return;
        }

        var adverse = pending.Side == Side.Buy
            ? (report.FillPrice - pending.IntendedPrice) / (double)pending.IntendedPrice
            : (pending.IntendedPrice - report.FillPrice) / (double)pending.IntendedPrice;
        var slipBps = Math.Max(0, adverse * 1e4);
        state.SlipEwmaBps += SlipAlpha * (slipBps - state.SlipEwmaBps);

        if (pending.IsExit || state.Open is not { } trade)
            return;
        trade.AnyFill = true;
        if (!trade.EntryPriceFromFill)
        {
            trade.EntryPrice = report.FillPrice;
            trade.EntryPriceFromFill = true;
        }
    }

    /// <summary>Settle the books when our exit order fills to flat.</summary>
    private void FinalizeClosedTrade(SymbolState state, in ExecutionReport report)
    {
        if (state.Open is not { } trade)
        {
            ClearFlat(state);
            return;
        }
        var exitPx = report.FillPrice > 0 ? report.FillPrice
            : state.Pending?.IntendedPrice ?? 0;
        var realized = exitPx > 0 && trade.EntryPrice > 0
            ? trade.Direction * Math.Log(exitPx / (double)trade.EntryPrice) * 1e4 - 2 * _commissionBps
            : 0;
        SettleRealized(state, realized);
    }

    /// <summary>Settle a position that went flat outside our order flow
    /// (manual close, stop-out): realize at the current exit-side mark.</summary>
    private void FinalizeExternallyClosedTrade(SymbolState state, in QuoteTick tick)
    {
        if (state.Open is not { } trade)
        {
            ClearFlat(state);
            return;
        }
        var exitPx = trade.Direction > 0 ? tick.Bid : tick.Ask;
        var realized = exitPx > 0 && trade.EntryPrice > 0
            ? trade.Direction * Math.Log(exitPx / (double)trade.EntryPrice) * 1e4 - 2 * _commissionBps
            : 0;
        SettleRealized(state, realized);
    }

    private void SettleRealized(SymbolState state, double realizedBps)
    {
        state.DayRealizedBps += realizedBps;
        if (realizedBps < 0)
        {
            state.ConsecutiveLosses++;
            state.LastLossExitBar = state.BarIndex;
        }
        else
        {
            state.ConsecutiveLosses = 0;
        }
        state.LastExitBar = state.BarIndex;
        ClearFlat(state);
    }

    private static void ClearFlat(SymbolState state)
    {
        state.Open = null;
        state.WantFlat = false;
        state.ExitRetry = false;
    }

    /// <summary>A position appeared without our entry (connect-time seed,
    /// manual trade): adopt it with conservative frozen parameters so the
    /// exit engine protects it immediately.</summary>
    private Trade AdoptExternalPosition(in StrategyContext ctx, in QuoteTick tick, SymbolState state)
    {
        var spreadNow = SpreadBps(tick.Bid, tick.Ask);
        var cost = RoundTripCostBps(state, double.IsFinite(spreadNow) ? spreadNow : 0);
        var sigma = double.IsFinite(state.LastSigmaBps) ? state.LastSigmaBps : double.NaN;
        var stopFloor = Math.Max(MinStopBps, StopCostFloorMult * cost);
        var trade = new Trade
        {
            Direction = Math.Sign(ctx.Position),
            EntryPrice = (tick.Bid / 2) + (tick.Ask / 2),
            AnyFill = true,
            EntryCostBps = cost,
            StopBps = double.IsFinite(sigma)
                ? Math.Clamp(_stopAtrMult * sigma, Math.Min(stopFloor, MaxStopBps), MaxStopBps)
                : MaxStopBps,
            Target0Bps = Math.Max(
                _costFloorMult * cost, _tpAtrMult * (double.IsFinite(sigma) ? sigma : 0)),
        };
        state.Open = trade;
        return trade;
    }

    private void Poison(in StrategyContext ctx, string reason, List<Intent> output)
    {
        _poisoned = true;
        _lastDecision = new StrategyDecision(double.NaN, DecisionStatus.Degraded, reason);
        CancelAll(ctx, output);
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

    private static void CancelAll(in StrategyContext ctx, List<Intent> output)
    {
        foreach (var order in ctx.OpenOrders)
            output.Add(new Intent.Cancel(order.OrderId, ctx.SymbolId));
    }
}

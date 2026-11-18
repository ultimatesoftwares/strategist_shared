using System.Collections.Concurrent;
using System.Threading.Channels;

using Trading.Core.Engine;

namespace Trading.Core;

/// <summary>
/// Runs the trading engine on a single logical thread, satisfying its
/// threading contract even when broker SDKs raise events from arbitrary
/// threads.
///
/// Broker events (market data, executions) are funneled into an unbounded
/// channel and drained by <see cref="RunAsync"/>. Engine order commands are
/// queued during <c>Publish*</c> calls (the callback must not re-enter the
/// engine) and routed to the broker afterwards.
///
/// Strategy orders optionally pass through an <see cref="IOrderGate"/>
/// (pre-trade risk); rejected orders are acknowledged back into the engine as
/// <see cref="OrderStatus.Rejected"/> so its open-order bookkeeping stays
/// consistent. External callers (REST control, OMS) interact with the engine
/// thread via <see cref="RunOnEngineLoopAsync"/>,
/// <see cref="PlaceManualOrderAsync"/> and <see cref="CancelOrderAsync"/>.
/// </summary>
public sealed class EngineHost
{
    private abstract record Event;
    private sealed record MarketDataEvent(BookUpdate Update) : Event;
    private sealed record BarEvent(Bar Bar) : Event;
    private sealed record ExecutionEvent(ExecutionReport Report) : Event;
    private sealed record ControlEvent(Action Action) : Event;

    /// <summary>Manual (operator/OMS) order ids live far above engine strategy ids.</summary>
    public const ulong ManualOrderIdBase = 1UL << 60;

    private readonly TradingEngine _engine;
    private readonly IBrokerAdapter _broker;
    private readonly IOrderGate? _orderGate;
    private readonly long _priceScale;
    private readonly string _name;
    private readonly bool _logFills;
    private readonly MarketDataRecorder? _recorder;

    private readonly Channel<Event> _events = Channel.CreateUnbounded<Event>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Queue<OrderCommand> _pendingCommands = new();
    // Written on the engine thread, safe to read from any thread.
    private readonly ConcurrentDictionary<uint, EngineSnapshot> _snapshots = new();
    // Last gate-rejection reason per symbol (engine thread only), to log
    // persistent conditions (market closed, kill switch) once instead of per
    // order. Cleared when a placement passes the gate again.
    private readonly Dictionary<uint, string> _lastGateReason = new();
    private ulong _nextManualOrderId = ManualOrderIdBase;

    public readonly record struct EngineSnapshot(TopOfBook TopOfBook, long Position);

    public long OrdersPlaced { get; private set; }
    public long OrdersCancelled { get; private set; }
    public long OrdersRejectedByGate { get; private set; }
    public long Fills { get; private set; }
    /// <summary>Realized cash flow in scaled price units * lots.</summary>
    public long RealizedCashFlow { get; private set; }

    /// <summary>Fired on the engine thread after an order command is sent to the broker.</summary>
    public event Action<OrderCommand>? OrderRouted;
    /// <summary>Fired on the engine thread when the order gate denies a placement.</summary>
    public event Action<OrderCommand.Place, string>? OrderGateRejected;
    /// <summary>Fired on the engine thread after an execution report is applied to the engine.</summary>
    public event Action<ExecutionReport>? ExecutionProcessed;

    /// <summary>
    /// The strategy's signal snapshot behind a book update that emitted at
    /// least one Place order. Captured before the commands are routed, so
    /// subscribers see the decision entry ahead of its orders (including
    /// gate rejects). PlaceOrderIds are the engine order ids the decision
    /// produced.
    /// </summary>
    public readonly record struct DecisionEvent(
        uint SymbolId, StrategyDecision Decision, TopOfBook TopOfBook,
        long Position, ulong TsNanos, ulong[] PlaceOrderIds)
    {
        /// <summary>
        /// The market state the strategy acted on, as a compact JSON object
        /// (prices descaled by <paramref name="priceScale"/>, invariant
        /// culture) for the decision journal's BookJson column.
        /// </summary>
        public string BuildBookJson(long priceScale) =>
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"bid\":{TopOfBook.BidPrice / (double)priceScale},\"bidQty\":{TopOfBook.BidQty},\"ask\":{TopOfBook.AskPrice / (double)priceScale},\"askQty\":{TopOfBook.AskQty},\"position\":{Position}}}");
    }

    /// <summary>
    /// Fired on the engine thread when a strategy decision emits one or more
    /// Place orders (deliberately not on every evaluation — that would be
    /// one event per book tick). Fires before the orders are routed.
    /// </summary>
    public event Action<DecisionEvent>? DecisionMade;

    public EngineHost(
        TradingEngine engine,
        IBrokerAdapter broker,
        long priceScale,
        string name = "",
        bool logFills = true,
        MarketDataRecorder? recorder = null,
        IOrderGate? orderGate = null)
    {
        _engine = engine;
        _broker = broker;
        _orderGate = orderGate;
        _priceScale = priceScale;
        _name = name;
        _logFills = logFills;
        _recorder = recorder;

        _broker.MarketData += u => _events.Writer.TryWrite(new MarketDataEvent(u));
        if (broker is IBarSource barSource)
            barSource.Bars += b => _events.Writer.TryWrite(new BarEvent(b));
        _broker.Execution += r => _events.Writer.TryWrite(new ExecutionEvent(r));
        _engine.OrderCommandReceived += c => _pendingCommands.Enqueue(c);
    }

    /// <summary>Process broker events until cancelled. Call from exactly one task.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var ev in _events.Reader.ReadAllAsync(ct))
            {
                switch (ev)
                {
                    case MarketDataEvent md:
                        _recorder?.Record(md.Update);
                        _engine.PublishBookUpdate(md.Update);
                        UpdateSnapshot(md.Update.SymbolId);
                        NotifyDecision(md.Update.SymbolId, md.Update.TsNanos);
                        break;
                    case BarEvent bar:
                        _engine.PublishBar(bar.Bar);
                        UpdateSnapshot(bar.Bar.SymbolId);
                        NotifyDecision(
                            bar.Bar.SymbolId, (ulong)bar.Bar.TimeUnixMs * 1_000_000);
                        break;
                    case ExecutionEvent ex:
                        _engine.PublishExecution(ex.Report);
                        UpdateSnapshot(ex.Report.SymbolId);
                        TrackExecution(ex.Report);
                        ExecutionProcessed?.Invoke(ex.Report);
                        break;
                    case ControlEvent ctl:
                        ctl.Action();
                        break;
                }
                DrainCommands();
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        finally
        {
            _recorder?.Flush();
        }
    }

    /// <summary>
    /// Raise <see cref="DecisionMade"/> when the event (book update or bar)
    /// that just ran produced Place orders. Allocation only happens on that
    /// (rare, order-emitting) path; pure evaluations cost two cheap checks.
    /// </summary>
    private void NotifyDecision(uint symbolId, ulong tsNanos)
    {
        if (_pendingCommands.Count == 0 || DecisionMade is null)
            return;
        if (!_engine.TryGetLastDecision(out var decision))
            return;

        var placeCount = 0;
        foreach (var cmd in _pendingCommands)
            if (cmd is OrderCommand.Place)
                placeCount++;
        if (placeCount == 0)
            return;

        var placeIds = new ulong[placeCount];
        var i = 0;
        foreach (var cmd in _pendingCommands)
            if (cmd is OrderCommand.Place p)
                placeIds[i++] = p.OrderId;

        var snapshot = GetSnapshot(symbolId);
        DecisionMade.Invoke(new DecisionEvent(
            symbolId, decision, snapshot.TopOfBook,
            snapshot.Position, tsNanos, placeIds));
    }

    private void DrainCommands()
    {
        while (_pendingCommands.TryDequeue(out var cmd))
        {
            switch (cmd)
            {
                case OrderCommand.Place p:
                    if (_orderGate?.Evaluate(p) is { Allowed: false, Reason: var reason })
                    {
                        RejectByGate(p, reason ?? "denied by risk gate");
                        break;
                    }
                    _lastGateReason.Remove(p.SymbolId);
                    OrdersPlaced++;
                    _broker.PlaceOrder(p);
                    OrderRouted?.Invoke(p);
                    break;
                case OrderCommand.Cancel c:
                    // Cancels are never gated: reducing exposure must always work.
                    OrdersCancelled++;
                    _broker.CancelOrder(c);
                    OrderRouted?.Invoke(c);
                    break;
            }
        }
    }

    private void RejectByGate(OrderCommand.Place p, string reason)
    {
        OrdersRejectedByGate++;
        // Log each distinct condition once per symbol: persistent blocks
        // (market closed, kill switch) would otherwise print per order.
        if (!_lastGateReason.TryGetValue(p.SymbolId, out var last) || last != reason)
        {
            _lastGateReason[p.SymbolId] = reason;
            Console.WriteLine(
                $"[{_name}] RISK BLOCK #{p.OrderId}: {reason} (repeats suppressed)");
        }
        OrderGateRejected?.Invoke(p, reason);
        // Acknowledge the rejection into the engine so it frees the open-order
        // slot. The engine may emit follow-up commands; they land in
        // _pendingCommands and are handled by the loop in DrainCommands.
        _engine.PublishExecution(new ExecutionReport(
            p.OrderId, p.SymbolId, OrderStatus.Rejected, p.Side,
            FillPrice: 0, FillQty: 0, LeavesQty: 0,
            TsNanos: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000));
    }

    /// <summary>
    /// Run <paramref name="func"/> on the engine thread and return its result.
    /// This is the only safe way for other threads to touch the engine.
    /// </summary>
    public Task<T> RunOnEngineLoopAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _events.Writer.TryWrite(new ControlEvent(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }));
        if (!queued)
            tcs.TrySetException(new InvalidOperationException("Engine loop is not accepting events."));
        return tcs.Task;
    }

    /// <summary>
    /// Place an operator/OMS-initiated order. It bypasses the strategy (and the
    /// order gate — the kill switch must not prevent flattening) but its fills
    /// still update the engine's position via the normal execution path.
    /// Returns the assigned manual order id.
    /// </summary>
    public Task<ulong> PlaceManualOrderAsync(uint symbolId, Side side, long price, ulong qty)
    {
        if (qty == 0)
            throw new ArgumentException("qty must be positive", nameof(qty));
        var orderId = Interlocked.Increment(ref _nextManualOrderId);
        return RunOnEngineLoopAsync(() =>
        {
            var place = new OrderCommand.Place(orderId, symbolId, side, price, qty);
            OrdersPlaced++;
            _broker.PlaceOrder(place);
            OrderRouted?.Invoke(place);
            return orderId;
        });
    }

    /// <summary>Request cancellation of any live order (strategy or manual) by id.</summary>
    public Task CancelOrderAsync(ulong orderId, uint symbolId) =>
        RunOnEngineLoopAsync(() =>
        {
            var cancel = new OrderCommand.Cancel(orderId, symbolId);
            OrdersCancelled++;
            _broker.CancelOrder(cancel);
            OrderRouted?.Invoke(cancel);
            return true;
        });

    /// <summary>Last known engine state for a symbol; readable from any thread.</summary>
    public EngineSnapshot GetSnapshot(uint symbolId) =>
        _snapshots.TryGetValue(symbolId, out var snapshot) ? snapshot : default;

    /// <summary>One position correction applied by <see cref="SyncPositionsAsync"/>.</summary>
    public readonly record struct PositionCorrection(uint SymbolId, long EngineLots, long BrokerLots);

    /// <summary>Aggregated L2 snapshot: best-first levels per side.</summary>
    public readonly record struct BookSnapshot(BookLevel[] Bids, BookLevel[] Asks, ulong LastTsNanos);

    /// <summary>
    /// Read up to <paramref name="depth"/> aggregated L2 levels per side from
    /// the engine's book, via the engine thread (safe from any caller).
    /// </summary>
    public Task<BookSnapshot> GetBookAsync(uint symbolId, int depth) =>
        RunOnEngineLoopAsync(() =>
        {
            var (bids, asks, lastTs) = _engine.GetBookLevels(symbolId, depth);
            return new BookSnapshot(bids, asks, lastTs);
        });

    /// <summary>
    /// Reconcile engine positions against broker-reported truth: for every
    /// entry, overwrite the engine's position when it differs. Runs on the
    /// engine thread, so it serializes correctly with in-flight executions
    /// already queued ahead of it. Returns the corrections applied.
    /// </summary>
    public Task<IReadOnlyList<PositionCorrection>> SyncPositionsAsync(
        IReadOnlyDictionary<uint, long> brokerPositions) =>
        RunOnEngineLoopAsync<IReadOnlyList<PositionCorrection>>(() =>
        {
            List<PositionCorrection>? corrections = null;
            foreach (var (symbolId, brokerLots) in brokerPositions)
            {
                var engineLots = _engine.GetPosition(symbolId);
                if (engineLots == brokerLots)
                    continue;
                _engine.SetPosition(symbolId, brokerLots);
                UpdateSnapshot(symbolId);
                (corrections ??= []).Add(new PositionCorrection(symbolId, engineLots, brokerLots));
            }
            return corrections ?? (IReadOnlyList<PositionCorrection>)[];
        });

    private void UpdateSnapshot(uint symbolId) =>
        _snapshots[symbolId] = new EngineSnapshot(
            _engine.GetTopOfBook(symbolId), _engine.GetPosition(symbolId));

    private void TrackExecution(in ExecutionReport report)
    {
        if (report.Status is not (OrderStatus.Filled or OrderStatus.PartiallyFilled))
            return;

        Fills++;
        var signed = (long)report.FillQty * report.FillPrice;
        RealizedCashFlow += report.Side == Side.Buy ? -signed : signed;

        if (_logFills)
        {
            Console.WriteLine(
                $"[{_name}] FILL  {report.Side,-4} {report.FillQty} @ {Px(report.FillPrice)}  " +
                $"pos={GetSnapshot(report.SymbolId).Position}");
        }
    }

    public string Px(long scaled) =>
        (scaled / (double)_priceScale).ToString("F" + Math.Max(2, (int)Math.Log10(_priceScale)));
}

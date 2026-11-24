using System.Collections.Concurrent;
using System.Diagnostics;
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
    private sealed record MarketDataEvent(BookUpdate Update, long EnqueuedAt) : Event;
    private sealed record QuoteEvent(QuoteTick Tick, long EnqueuedAt) : Event;
    private sealed record QuoteWatchdogEvent : Event;
    private sealed record BarEvent(Bar Bar) : Event;
    private sealed record ExecutionEvent(ExecutionReport Report, long ReceivedAt) : Event;
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
    private readonly LatencyHistogram _marketQueueLatency = new();
    private readonly LatencyHistogram _marketProcessingLatency = new();
    private readonly LatencyHistogram _brokerSubmissionLatency = new();
    private readonly LatencyHistogram _brokerResponseLatency = new();
    private readonly LatencyHistogram _brokerFillLatency = new();
    private readonly LatencyHistogram _quoteToSubmissionLatency = new();
    private readonly LatencyHistogram _quoteToResponseLatency = new();
    private readonly LatencyHistogram _quoteToFillLatency = new();
    private const int MaxTimedOrders = 100_000;
    private sealed class OrderTiming(long started, long? quoteOrigin, LinkedListNode<ulong> node)
    {
        public long Started { get; } = started;
        public long? QuoteOrigin { get; } = quoteOrigin;
        public LinkedListNode<ulong> Node { get; } = node;
        public bool ResponseObserved;
        public bool FillObserved;
    }
    private readonly Dictionary<ulong, OrderTiming> _orderTimings = [];
    private readonly LinkedList<ulong> _timedOrderIds = [];
    private long _orderTimingEvictions;
    private long _expiredQuotes;
    private static readonly TimeSpan QuoteDeadline = TimeSpan.FromMilliseconds(100);
    /// <summary>Strategy-declared quote-silence tolerance (min 1s): longer
    /// silences invalidate the symbol via <see cref="TradingEngine.PublishQuoteGap"/>.</summary>
    private readonly TimeSpan _quoteGap;
    private readonly Dictionary<uint, long> _lastQuoteArrival = [];
    private int _watchdogQueued;

    private readonly Channel<Event> _events = Channel.CreateUnbounded<Event>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Queue<OrderCommand> _pendingCommands = new();
    // Written on the engine thread, safe to read from any thread.
    private readonly ConcurrentDictionary<uint, EngineSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<uint, BarDecisionSnapshot> _barDecisions = new();
    private readonly ConcurrentDictionary<uint, TickDecisionSnapshot> _tickDecisions = new();
    private readonly Dictionary<uint, long> _lastTickDecisionPublication = [];
    // Last gate-rejection reason per symbol (engine thread only), to log
    // persistent conditions (market closed, kill switch) once instead of per
    // order. Cleared when a placement passes the gate again.
    private readonly Dictionary<uint, string> _lastGateReason = new();
    // Last engine risk-veto reason per symbol. Written on the engine
    // thread, read from API threads; logged once per distinct condition
    // (like _lastGateReason) and cleared when an intent passes risk again.
    private readonly ConcurrentDictionary<uint, string> _riskVetoes = new();
    private ulong _nextManualOrderId = ManualOrderIdBase;

    public readonly record struct EngineSnapshot(TopOfBook TopOfBook, long Position);
    public sealed record BarDecisionSnapshot(
        Bar Bar, StrategyDecision Decision, DateTimeOffset ObservedUtc, string? OrderOutcome);
    public sealed record TickDecisionSnapshot(
        ulong QuoteTsNanos, StrategyDecision Decision, DateTimeOffset ObservedUtc);

    public BarDecisionSnapshot? GetBarDecision(uint symbolId) =>
        _barDecisions.GetValueOrDefault(symbolId);
    public TickDecisionSnapshot? GetTickDecision(uint symbolId) =>
        _tickDecisions.GetValueOrDefault(symbolId);

    /// <summary>The symbol's most recent engine risk-veto reason, until an
    /// intent passes risk again; null = none. Safe from any thread.</summary>
    public string? GetRiskVeto(uint symbolId) =>
        _riskVetoes.GetValueOrDefault(symbolId);

    /// <summary>Process-lifetime timing diagnostics for this host.</summary>
    public EngineLatencySnapshot GetLatencySnapshot() =>
        new(_marketQueueLatency.Snapshot(), _marketProcessingLatency.Snapshot(), _brokerSubmissionLatency.Snapshot(),
            Interlocked.Read(ref _expiredQuotes), _brokerResponseLatency.Snapshot(), _brokerFillLatency.Snapshot(),
            Interlocked.Read(ref _orderTimingEvictions), _broker is ISyntheticExecutionSource,
            _quoteToSubmissionLatency.Snapshot(), _quoteToResponseLatency.Snapshot(), _quoteToFillLatency.Snapshot());

    public long OrdersPlaced { get; private set; }
    public long OrdersCancelled { get; private set; }
    public long OrdersRejectedByGate { get; private set; }
    /// <summary>Strategy intents vetoed by the engine's pre-trade risk checks.</summary>
    public long OrdersRejectedByRisk { get; private set; }
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

    /// <summary>Fired on the engine thread after a bar strategy evaluated a
    /// closed bar: symbol, bar, the strategy's decision, and whether it
    /// emitted orders.</summary>
    public event Action<Bar, StrategyDecision, bool>? BarEvaluated;

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
        var tolerance = engine.QuoteGapTolerance;
        _quoteGap = tolerance > TimeSpan.FromSeconds(1) ? tolerance : TimeSpan.FromSeconds(1);

        if (engine.RequiresAtomicQuotes)
        {
            if (broker is not IQuoteTickSource { SupportsQuoteTicks: true } ticks)
                throw new InvalidOperationException("strategy requires an atomic quote feed from the broker");
            ticks.QuoteTick += q => _events.Writer.TryWrite(new QuoteEvent(q, Stopwatch.GetTimestamp()));
            if (_recorder is not null)
                _broker.MarketData += u => _events.Writer.TryWrite(new MarketDataEvent(u, Stopwatch.GetTimestamp()));
        }
        else
            _broker.MarketData += u => _events.Writer.TryWrite(new MarketDataEvent(u, Stopwatch.GetTimestamp()));
        if (!engine.RequiresAtomicQuotes && broker is IBarSource barSource)
            barSource.Bars += b => _events.Writer.TryWrite(new BarEvent(b));
        _broker.Execution += r => _events.Writer.TryWrite(new ExecutionEvent(r, Stopwatch.GetTimestamp()));
        _engine.OrderCommandReceived += c =>
        {
            _riskVetoes.TryRemove(c.SymbolId, out _);
            _pendingCommands.Enqueue(c);
        };
        _engine.OrderCommandRejected += (command, verdict) =>
        {
            OrdersRejectedByRisk++;
            var reason = verdict.ToString();
            if (_riskVetoes.TryGetValue(command.SymbolId, out var last) && last == reason)
                return;
            _riskVetoes[command.SymbolId] = reason;
            Console.WriteLine(
                $"[{_name}] ENGINE RISK VETO {command.GetType().Name} on "
                + $"{_engine.SymbolName(command.SymbolId) ?? command.SymbolId.ToString()}: {reason} (repeats suppressed)");
        };
    }

    /// <summary>Process broker events until cancelled. Call from exactly one task.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var watchdogStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = _engine.RequiresAtomicQuotes ? RunQuoteWatchdogAsync(watchdogStop.Token) : Task.CompletedTask;
        try
        {
            await foreach (var ev in _events.Reader.ReadAllAsync(ct))
            {
                long? quoteOrigin = null;
                uint quoteSymbol = 0;
                switch (ev)
                {
                    case MarketDataEvent md:
                        var started = Stopwatch.GetTimestamp();
                        _marketQueueLatency.Observe(started - md.EnqueuedAt);
                        _recorder?.Record(md.Update);
                        if (!_engine.RequiresAtomicQuotes)
                        {
                            _engine.PublishBookUpdate(md.Update);
                            UpdateSnapshot(md.Update.SymbolId);
                            NotifyDecision(md.Update.SymbolId, md.Update.TsNanos);
                        }
                        _marketProcessingLatency.Observe(Stopwatch.GetTimestamp() - started);
                        break;
                    case QuoteEvent quote:
                        var quoteStarted = Stopwatch.GetTimestamp();
                        _marketQueueLatency.Observe(quoteStarted - quote.EnqueuedAt);
                        if (_lastQuoteArrival.TryGetValue(quote.Tick.SymbolId, out var previousQuote)
                            && Stopwatch.GetElapsedTime(previousQuote, quote.EnqueuedAt) > _quoteGap)
                            _engine.PublishQuoteGap(quote.Tick.SymbolId);
                        _lastQuoteArrival[quote.Tick.SymbolId] = quote.EnqueuedAt;
                        if (Stopwatch.GetElapsedTime(quote.EnqueuedAt, quoteStarted) > QuoteDeadline)
                        {
                            Interlocked.Increment(ref _expiredQuotes);
                            _engine.PublishQuoteGap(quote.Tick.SymbolId);
                        }
                        else
                        {
                            _engine.PublishTick(quote.Tick);
                            if (Stopwatch.GetElapsedTime(quote.EnqueuedAt) > QuoteDeadline)
                            {
                                Interlocked.Increment(ref _expiredQuotes);
                                _engine.PublishQuoteGap(quote.Tick.SymbolId);
                                RejectExpiredCommands();
                            }
                            else
                            {
                                quoteOrigin = quote.EnqueuedAt;
                                quoteSymbol = quote.Tick.SymbolId;
                                UpdateSnapshot(quote.Tick.SymbolId);
                                NotifyDecision(quote.Tick.SymbolId, quote.Tick.TsNanos);
                            }
                        }
                        UpdateSnapshot(quote.Tick.SymbolId);
                        PublishTickDecision(quote.Tick.SymbolId, quote.Tick.TsNanos);
                        _marketProcessingLatency.Observe(Stopwatch.GetTimestamp() - quoteStarted);
                        break;
                    case QuoteWatchdogEvent:
                        Interlocked.Exchange(ref _watchdogQueued, 0);
                        var watchdogNow = Stopwatch.GetTimestamp();
                        foreach (var symbol in _lastQuoteArrival
                            .Where(p => Stopwatch.GetElapsedTime(p.Value, watchdogNow) > _quoteGap)
                            .Select(p => p.Key).ToArray())
                        {
                            _lastQuoteArrival.Remove(symbol);
                            _engine.PublishQuoteGap(symbol);
                            UpdateSnapshot(symbol);
                            PublishTickDecision(symbol, 0);
                        }
                        break;
                    case BarEvent bar:
                        _engine.PublishBar(bar.Bar);
                        UpdateSnapshot(bar.Bar.SymbolId);
                        var hasDecision = _engine.TryGetLastDecision(out var barDecision);
                        if (BarEvaluated is not null && hasDecision)
                            BarEvaluated(bar.Bar, barDecision, _pendingCommands.Count > 0);
                        NotifyDecision(
                            bar.Bar.SymbolId, (ulong)bar.Bar.TimeUnixMs * 1_000_000);
                        var placedBefore = OrdersPlaced;
                        var blockedBefore = OrdersRejectedByGate;
                        DrainCommands();
                        if (hasDecision)
                        {
                            var outcome = OrdersRejectedByGate > blockedBefore
                                ? $"Blocked: {_lastGateReason.GetValueOrDefault(bar.Bar.SymbolId) ?? "risk gate"}"
                                : OrdersPlaced > placedBefore ? "Order routed; check routing mode and latest order status."
                                : barDecision.Reason is "Long entry signal." or "Short entry signal." or "Exit signal." or "Exit signal: reversion, stop or maximum hold."
                                    ? "No order sent; check the engine risk limits and available quote."
                                    : null;
                            _barDecisions[bar.Bar.SymbolId] = new(bar.Bar, barDecision, DateTimeOffset.UtcNow, outcome);
                        }
                        break;
                    case ExecutionEvent ex:
                        ObserveOrderTiming(ex.Report, ex.ReceivedAt);
                        _engine.PublishExecution(ex.Report);
                        UpdateSnapshot(ex.Report.SymbolId);
                        if (_broker is IExecutionAppliedObserver observer)
                            observer.OnExecutionApplied(ex.Report);
                        TrackExecution(ex.Report);
                        ExecutionProcessed?.Invoke(ex.Report);
                        break;
                    case ControlEvent ctl:
                        ctl.Action();
                        break;
                }
                DrainCommands(quoteOrigin, quoteSymbol);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        finally
        {
            watchdogStop.Cancel();
            await watchdog;
            _recorder?.Flush();
        }
    }

    private async Task RunQuoteWatchdogAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                if (Interlocked.CompareExchange(ref _watchdogQueued, 1, 0) == 0
                    && !_events.Writer.TryWrite(new QuoteWatchdogEvent()))
                    Interlocked.Exchange(ref _watchdogQueued, 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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

    private void PublishTickDecision(uint symbolId, ulong tsNanos)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastTickDecisionPublication.TryGetValue(symbolId, out var previous)
            && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(250)
            && _pendingCommands.Count == 0
            && _engine.TryGetLastDecision(out var current)
            && current.Status != DecisionStatus.Degraded)
            return;
        if (!_engine.TryGetLastDecision(out var decision))
            return;
        _lastTickDecisionPublication[symbolId] = now;
        _tickDecisions[symbolId] = new TickDecisionSnapshot(tsNanos, decision, DateTimeOffset.UtcNow);
    }

    private void DrainCommands(long? quoteOrigin = null, uint quoteSymbol = 0)
    {
        while (_pendingCommands.TryDequeue(out var cmd))
        {
            switch (cmd)
            {
                case OrderCommand.Place p:
                    if (_engine.RequiresAtomicQuotes &&
                        (quoteOrigin is null || p.SymbolId != quoteSymbol ||
                         Stopwatch.GetElapsedTime(quoteOrigin.Value) > QuoteDeadline))
                    {
                        RejectStaleQuoteOrder(p, quoteOrigin, quoteSymbol);
                        quoteOrigin = null;
                        break;
                    }
                    if (_orderGate?.Evaluate(p) is { Allowed: false, Reason: var reason })
                    {
                        RejectByGate(p, reason ?? "denied by risk gate");
                        break;
                    }

                    if (_engine.RequiresAtomicQuotes && quoteOrigin is { } origin &&
                        Stopwatch.GetElapsedTime(origin) > QuoteDeadline)
                    {
                        RejectStaleQuoteOrder(p, quoteOrigin, quoteSymbol);
                        quoteOrigin = null;
                        break;
                    }
                    _lastGateReason.Remove(p.SymbolId);
                    OrdersPlaced++;
                    var submissionStarted = Stopwatch.GetTimestamp();
                    if (quoteOrigin is { } quoteStarted)
                        _quoteToSubmissionLatency.Observe(Math.Max(0, submissionStarted - quoteStarted));
                    StartOrderTiming(p.OrderId, submissionStarted, quoteOrigin);
                    if (_broker is IPositionAwareBroker positionAware)
                        positionAware.PlaceOrder(p, _engine.GetPosition(p.SymbolId));
                    else
                        _broker.PlaceOrder(p);
                    _brokerSubmissionLatency.Observe(Stopwatch.GetTimestamp() - submissionStarted);
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

    private void RejectStaleQuoteOrder(OrderCommand.Place place, long? origin, uint symbol)
    {
        if (origin is { } started && Stopwatch.GetElapsedTime(started) > QuoteDeadline)
        {
            Interlocked.Increment(ref _expiredQuotes);
            _engine.PublishQuoteGap(symbol);
            UpdateSnapshot(symbol);
            PublishTickDecision(symbol, 0);
        }
        RejectExpiredCommands(place);
    }

    private void RejectExpiredCommands(OrderCommand.Place? current = null)
    {
        var commands = _pendingCommands.ToArray();
        _pendingCommands.Clear();
        var unsent = commands.OfType<OrderCommand.Place>().Select(p => p.OrderId).ToHashSet();
        if (current is not null)
        {
            unsent.Add(current.OrderId);
            RejectByGate(current, "quote processing deadline exceeded or quote unavailable");
        }
        foreach (var command in commands)
            if (command is OrderCommand.Place place)
                RejectByGate(place, "quote processing deadline exceeded");
            else if (!unsent.Contains(command.OrderId))
                _pendingCommands.Enqueue(command);
    }

    private void StartOrderTiming(ulong orderId, long started, long? quoteOrigin)
    {
        if (_broker is ISyntheticExecutionSource)
            return;
        if (_orderTimings.Count == MaxTimedOrders)
        {
            var oldest = _timedOrderIds.First!;
            _orderTimings.Remove(oldest.Value);
            _timedOrderIds.RemoveFirst();
            Interlocked.Increment(ref _orderTimingEvictions);
        }
        var node = _timedOrderIds.AddLast(orderId);
        _orderTimings.Add(orderId, new OrderTiming(started, quoteOrigin, node));
    }

    private void ObserveOrderTiming(in ExecutionReport report, long receivedAt)
    {
        if (!_orderTimings.TryGetValue(report.OrderId, out var timing))
            return;
        if (!timing.ResponseObserved)
        {
            _brokerResponseLatency.Observe(Math.Max(0, receivedAt - timing.Started));
            if (timing.QuoteOrigin is { } quoteOrigin)
                _quoteToResponseLatency.Observe(Math.Max(0, receivedAt - quoteOrigin));
            timing.ResponseObserved = true;
        }
        if (!timing.FillObserved && report.FillQty > 0
            && report.Status is OrderStatus.PartiallyFilled or OrderStatus.Filled)
        {
            _brokerFillLatency.Observe(Math.Max(0, receivedAt - timing.Started));
            if (timing.QuoteOrigin is { } quoteOrigin)
                _quoteToFillLatency.Observe(Math.Max(0, receivedAt - quoteOrigin));
            timing.FillObserved = true;
        }
        if (report.Status is OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Cancelled)
        {
            _orderTimings.Remove(report.OrderId);
            _timedOrderIds.Remove(timing.Node);
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
            if (_broker is IPositionAwareBroker positionAware)
                positionAware.PlaceOrder(place, _engine.GetPosition(symbolId));
            else
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

    /// <summary>
    /// Export the strategy's warm feature window per symbol (symbol name →
    /// closed bars, oldest first) for checkpointing. Empty when the strategy
    /// keeps no exportable warm state. Marshalled onto the engine loop.
    /// </summary>
    public Task<IReadOnlyDictionary<string, WarmupBarSnapshot[]>> ExportWarmStateAsync() =>
        RunOnEngineLoopAsync<IReadOnlyDictionary<string, WarmupBarSnapshot[]>>(() =>
        {
            var result = new Dictionary<string, WarmupBarSnapshot[]>();
            var buffer = new List<WarmupBarSnapshot>();
            for (uint id = 0; id < _engine.SymbolCount; id++)
            {
                buffer.Clear();
                if (_engine.ExportWarmBars(id, buffer) > 0 && _engine.SymbolName(id) is { } name)
                    result[name] = [.. buffer];
            }
            return result;
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

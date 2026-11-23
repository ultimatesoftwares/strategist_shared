namespace Trading.Core.Engine;

/// <summary>
/// The trading engine: owns books, positions and open orders; runs the
/// strategy and pre-trade risk checks over every market data event.
///
/// Threading contract: all calls on one instance must come from a single
/// thread. Broker SDK callbacks typically arrive on arbitrary threads —
/// marshal them onto one engine thread (e.g. via a
/// <see cref="System.Threading.Channels.Channel{T}"/>) before calling in.
///
/// <see cref="OrderCommandReceived"/> fires synchronously, on the same
/// thread, while <see cref="PublishBookUpdate"/> or
/// <see cref="PublishExecution"/> is executing. Handlers must not re-enter
/// the engine; enqueue broker work instead.
/// </summary>
public sealed class TradingEngine : IDisposable
{
    private readonly List<string> _symbols = [];
    private readonly Dictionary<uint, OrderBook> _books = [];
    private readonly Dictionary<uint, long> _positions = [];
    private readonly Dictionary<ulong, OpenOrder> _openOrders = [];
    private readonly RiskChecker _risk;
    private readonly IStrategy _strategy;
    private ulong _nextOrderId = 1;
    // Reused scratch buffers to avoid per-event allocation.
    private readonly List<Intent> _intentBuffer = new(8);
    private readonly List<OpenOrder> _openBuffer = new(8);
    private bool _disposed;

    public event Action<OrderCommand>? OrderCommandReceived;
    public bool RequiresAtomicQuotes => _strategy.RequiresAtomicQuotes;

    /// <summary>
    /// Create the engine with the given limits and strategy. Throws
    /// <see cref="InvalidOperationException"/> when the strategy
    /// configuration is invalid (unknown kind, missing/broken ONNX model,
    /// out-of-range thresholds).
    /// </summary>
    public TradingEngine(RiskLimits limits, StrategyConfig? strategy = null)
    {
        _risk = new RiskChecker(limits);
        _strategy = StrategyFactory.Create(strategy ?? StrategyConfig.Default);
    }

    /// <summary>Create the engine over an explicit strategy instance (research/testing).</summary>
    public TradingEngine(RiskLimits limits, IStrategy strategy)
    {
        _risk = new RiskChecker(limits);
        _strategy = strategy;
    }

    /// <summary>Register a tradable symbol; returns the id used on the hot path.</summary>
    public uint RegisterSymbol(string name)
    {
        ThrowIfDisposed();
        var existing = _symbols.IndexOf(name);
        if (existing >= 0)
            return (uint)existing;
        var id = (uint)_symbols.Count;
        _symbols.Add(name);
        _books[id] = new OrderBook();
        _positions[id] = 0;
        return id;
    }

    public string? SymbolName(uint symbolId) =>
        symbolId < _symbols.Count ? _symbols[(int)symbolId] : null;

    /// <summary>Feed a market data delta; commands fire synchronously via the event.</summary>
    public void PublishBookUpdate(in BookUpdate update)
    {
        ThrowIfDisposed();
        if (!_books.TryGetValue(update.SymbolId, out var book))
            return;
        book.Apply(update);
        var ctx = BuildContext(update.SymbolId, book);
        _intentBuffer.Clear();
        _strategy.OnBook(ctx, _intentBuffer);
        ProcessIntents();
    }

    /// <summary>
    /// Feed a top-of-book quote tick: the symbol's book is replaced by the
    /// quote atomically (tick feeds are touch-only; see
    /// <see cref="OrderBook.SetTouch"/>) so the strategy never sees a
    /// half-applied crossed book, then evaluated exactly once via
    /// <see cref="IStrategy.OnTick"/> — whose default delegates to
    /// <see cref="IStrategy.OnBook"/>. Commands fire synchronously via the
    /// event.
    /// </summary>
    public void PublishTick(in QuoteTick tick)
    {
        ThrowIfDisposed();
        if (!_books.TryGetValue(tick.SymbolId, out var book))
            return;
        book.SetTouch(tick.Bid, tick.BidQty, tick.Ask, tick.AskQty, tick.TsNanos);
        var ctx = BuildContext(tick.SymbolId, book);
        _intentBuffer.Clear();
        _strategy.OnTick(ctx, tick, _intentBuffer);
        ProcessIntents();
    }

    public void PublishQuoteGap(uint symbolId)
    {
        ThrowIfDisposed();
        if (!_books.TryGetValue(symbolId, out var book))
            return;
        book.Clear();
        var ctx = BuildContext(symbolId, book);
        _intentBuffer.Clear();
        _strategy.OnQuoteGap(ctx, _intentBuffer);
        ProcessIntents();
    }

    /// <summary>Closed bars the strategy needs before its first decision.</summary>
    public int StrategyWarmupBars => _strategy.WarmupBars;

    /// <summary>
    /// Pre-feed a historical bar for warm-up: the strategy folds it into its
    /// state but cannot emit orders. Call on the engine thread (or before
    /// the host loop starts), oldest bar first.
    /// </summary>
    public void WarmBar(in Bar bar)
    {
        ThrowIfDisposed();
        _strategy.WarmBar(bar);
    }

    /// <summary>
    /// Feed a closed bar (the book is not touched; bar strategies keep
    /// their own history). Commands fire synchronously via the event.
    /// </summary>
    public void PublishBar(in Bar bar)
    {
        ThrowIfDisposed();
        if (!_books.TryGetValue(bar.SymbolId, out var book))
            return;
        var ctx = BuildContext(bar.SymbolId, book);
        _intentBuffer.Clear();
        _strategy.OnBar(ctx, bar, _intentBuffer);
        ProcessIntents();
    }

    /// <summary>Feed a broker execution report back into the engine.</summary>
    public void PublishExecution(in ExecutionReport report)
    {
        ThrowIfDisposed();
        switch (report.Status)
        {
            case OrderStatus.Accepted:
                if (_openOrders.TryGetValue(report.OrderId, out var accepted))
                    _openOrders[report.OrderId] = accepted with { LeavesQty = report.LeavesQty };
                break;
            case OrderStatus.PartiallyFilled:
            case OrderStatus.Filled:
                var signed = report.Side == Side.Buy
                    ? (long)report.FillQty
                    : -(long)report.FillQty;
                _positions[report.SymbolId] =
                    _positions.GetValueOrDefault(report.SymbolId) + signed;
                if (report.Status == OrderStatus.Filled || report.LeavesQty == 0)
                    _openOrders.Remove(report.OrderId);
                else if (_openOrders.TryGetValue(report.OrderId, out var partial))
                    _openOrders[report.OrderId] = partial with { LeavesQty = report.LeavesQty };
                break;
            case OrderStatus.Cancelled:
            case OrderStatus.Rejected:
                _openOrders.Remove(report.OrderId);
                break;
        }

        _intentBuffer.Clear();
        _strategy.OnExecution(report, _intentBuffer);
        ProcessIntents();
    }

    public long GetPosition(uint symbolId)
    {
        ThrowIfDisposed();
        return _positions.GetValueOrDefault(symbolId);
    }

    /// <summary>
    /// Overwrite the net position for a registered symbol (broker
    /// reconciliation). The engine's position is otherwise pure fill
    /// accounting, which drifts when fill events are lost or positions are
    /// closed outside the engine (manual close, SL/TP, stop-out).
    /// </summary>
    public void SetPosition(uint symbolId, long position)
    {
        ThrowIfDisposed();
        if (!_books.ContainsKey(symbolId))
            throw new InvalidOperationException($"unknown symbol id {symbolId}");
        _positions[symbolId] = position;
    }

    public TopOfBook GetTopOfBook(uint symbolId)
    {
        ThrowIfDisposed();
        if (!_books.TryGetValue(symbolId, out var book))
            throw new InvalidOperationException($"unknown symbol id {symbolId}");
        book.TryGetBestBid(out var bidPrice, out var bidQty);
        book.TryGetBestAsk(out var askPrice, out var askQty);
        return new TopOfBook(bidPrice, bidQty, askPrice, askQty);
    }

    /// <summary>
    /// Read up to <paramref name="maxLevels"/> aggregated L2 levels per side,
    /// best-first (bids descending, asks ascending), plus the book's last
    /// update timestamp in nanoseconds.
    /// </summary>
    public (BookLevel[] Bids, BookLevel[] Asks, ulong LastTsNanos) GetBookLevels(
        uint symbolId, int maxLevels)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLevels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLevels, 256);
        if (!_books.TryGetValue(symbolId, out var book))
            throw new InvalidOperationException($"unknown symbol id {symbolId}");

        var (bidDepth, askDepth) = book.Depth();
        var bids = new BookLevel[Math.Min(maxLevels, bidDepth)];
        var asks = new BookLevel[Math.Min(maxLevels, askDepth)];
        book.TopBids(bids, bids.Length);
        book.TopAsks(asks, asks.Length);
        return (bids, asks, book.LastTsNanos);
    }

    /// <summary>
    /// Read the strategy's signal snapshot behind its most recent
    /// evaluation (book, tick or bar). Returns false when the strategy
    /// exposes no decision (NullStrategy, or nothing evaluated yet). Call
    /// right after the publishing call, on the engine thread.
    /// </summary>
    public bool TryGetLastDecision(out StrategyDecision decision)
    {
        ThrowIfDisposed();
        if (_strategy.LastDecision is { } last)
        {
            decision = last;
            return true;
        }
        decision = default;
        return false;
    }

    public int OpenOrderCount => _openOrders.Count;

    private StrategyContext BuildContext(uint symbolId, OrderBook book)
    {
        _openBuffer.Clear();
        foreach (var order in _openOrders.Values)
        {
            if (order.SymbolId == symbolId)
                _openBuffer.Add(order);
        }
        return new StrategyContext(
            symbolId, book, _positions.GetValueOrDefault(symbolId), _openBuffer);
    }

    /// <summary>
    /// Convert buffered intents into risk-checked commands, tracking open
    /// orders, and fire <see cref="OrderCommandReceived"/> per command.
    /// </summary>
    private void ProcessIntents()
    {
        foreach (var intent in _intentBuffer)
        {
            OrderCommand command = intent switch
            {
                Intent.Place place => new OrderCommand.Place(
                    _nextOrderId, place.SymbolId, place.Side, place.Price, place.Qty),
                Intent.Cancel cancel => new OrderCommand.Cancel(cancel.OrderId, cancel.SymbolId),
                _ => throw new InvalidOperationException($"unknown intent {intent}"),
            };

            var position = _positions.GetValueOrDefault(command.SymbolId);
            if (_risk.Check(command, position, _openOrders) != RiskVerdict.Approved)
                continue;

            if (command is OrderCommand.Place approved)
            {
                _nextOrderId++;
                _openOrders[approved.OrderId] = new OpenOrder(
                    approved.OrderId, approved.SymbolId, approved.Side, approved.Price,
                    approved.Qty);
            }
            OrderCommandReceived?.Invoke(command);
        }
        _intentBuffer.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        (_strategy as IDisposable)?.Dispose();
    }
}

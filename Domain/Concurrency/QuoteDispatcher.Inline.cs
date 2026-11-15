namespace Core.Concurrency;

/// <summary>
/// Inline dispatch mode — the backtest path. The SAME sequence as the live
/// path (sequence number → snapshot write → consumer), but the consumer is
/// invoked synchronously on the caller's thread: no rings, no waiters, no
/// threads, no drops. Live and backtest therefore share one ordering
/// contract ("a consumer never observes a snapshot older than the tick it
/// is handling") while backtests stay strictly deterministic.
/// </summary>
public sealed partial class QuoteDispatcher
{
    /// <summary>Synchronous tick consumer. Struct passed by readonly ref —
    /// no copies, no boxing, no allocation.</summary>
    public delegate void InlineHandler(in QuoteTick tick);

    private InlineHandler? _inline;

    /// <summary>Routes every subsequent <see cref="DispatchInline"/> call to
    /// the handler. Set once before the replay starts.</summary>
    public void SetInlineHandler(InlineHandler handler) => _inline = handler;

    /// <summary>
    /// The backtest tick path: sequence → snapshot write → handler, all on
    /// the calling thread. Identical ordering to <see cref="Dispatch"/>;
    /// deterministic because nothing is concurrent and nothing can drop.
    /// </summary>
    public void DispatchInline(double bid, double ask, long tsMs)
    {
        long seq = ++_seq;
        _table.Write(_slot, bid, ask, tsMs, seq);

        var tick = new QuoteTick
        {
            VenueSlot = _slot,
            SymbolId = _symbolId,
            Bid = bid,
            Ask = ask,
            TsMs = tsMs,
            Seq = seq,
            Epoch = Volatile.Read(ref _epoch), // 0 forever in backtests
        };
        _inline?.Invoke(in tick);
    }
}

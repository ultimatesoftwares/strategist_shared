namespace Trading.Core.Engine;

/// <summary>
/// Price-level aggregated L2 order book for one symbol.
///
/// Prices are fixed-point integers scaled by the instrument's price scale;
/// quantities are lot units. Bids iterate best-first descending, asks
/// best-first ascending.
/// </summary>
public sealed class OrderBook
{
    // SortedList keeps keys ascending with O(1) indexed access, which the
    // top-of-book reads and best-first iteration exploit. Books on this
    // path are shallow (tick feeds carry one level per side), so the O(n)
    // array shift on insert/remove is irrelevant.
    private readonly SortedList<long, ulong> _bids = [];
    private readonly SortedList<long, ulong> _asks = [];

    public ulong LastTsNanos { get; private set; }

    /// <summary>Apply an L2 delta. A qty of 0 deletes the level.</summary>
    public void Apply(in BookUpdate update)
    {
        var side = update.Side == Side.Buy ? _bids : _asks;
        if (update.Qty == 0)
            side.Remove(update.Price);
        else
            side[update.Price] = update.Qty;
        LastTsNanos = update.TsNanos;
    }

    public void Clear()
    {
        _bids.Clear();
        _asks.Clear();
    }

    /// <summary>
    /// Replace the whole book with a top-of-book quote (tick feeds are
    /// touch-only: stale depth would be a lie). Non-positive prices skip
    /// their side; a 0 qty means "size unknown" and is stored as 1 so book
    /// math (imbalance, top qty) stays defined.
    /// </summary>
    public void SetTouch(long bid, ulong bidQty, long ask, ulong askQty, ulong tsNanos)
    {
        _bids.Clear();
        _asks.Clear();
        if (bid > 0)
            _bids[bid] = Math.Max(bidQty, 1);
        if (ask > 0)
            _asks[ask] = Math.Max(askQty, 1);
        LastTsNanos = tsNanos;
    }

    public bool TryGetBestBid(out long price, out ulong qty)
    {
        if (_bids.Count == 0)
        {
            price = 0;
            qty = 0;
            return false;
        }
        price = _bids.Keys[_bids.Count - 1];
        qty = _bids.Values[_bids.Count - 1];
        return true;
    }

    public bool TryGetBestAsk(out long price, out ulong qty)
    {
        if (_asks.Count == 0)
        {
            price = 0;
            qty = 0;
            return false;
        }
        price = _asks.Keys[0];
        qty = _asks.Values[0];
        return true;
    }

    /// <summary>Best-first bid levels (highest price first), at most <paramref name="max"/>.</summary>
    public int TopBids(Span<BookLevel> levels, int max)
    {
        var count = Math.Min(Math.Min(max, levels.Length), _bids.Count);
        for (var i = 0; i < count; i++)
        {
            var index = _bids.Count - 1 - i;
            levels[i] = new BookLevel(_bids.Keys[index], _bids.Values[index]);
        }
        return count;
    }

    /// <summary>Best-first ask levels (lowest price first), at most <paramref name="max"/>.</summary>
    public int TopAsks(Span<BookLevel> levels, int max)
    {
        var count = Math.Min(Math.Min(max, levels.Length), _asks.Count);
        for (var i = 0; i < count; i++)
            levels[i] = new BookLevel(_asks.Keys[i], _asks.Values[i]);
        return count;
    }

    /// <summary>Mid price in scaled units, if both sides are present.</summary>
    public long? Mid() =>
        TryGetBestBid(out var bid, out _) && TryGetBestAsk(out var ask, out _)
            ? (bid + ask) / 2
            : null;

    public long? Spread() =>
        TryGetBestBid(out var bid, out _) && TryGetBestAsk(out var ask, out _)
            ? ask - bid
            : null;

    /// <summary>
    /// Top-of-book size imbalance in basis points of total size:
    /// +10_000 = all bid, -10_000 = all ask, 0 = balanced.
    /// </summary>
    public long? ImbalanceBps()
    {
        if (!TryGetBestBid(out _, out var bidQty) || !TryGetBestAsk(out _, out var askQty))
            return null;
        var total = bidQty + askQty;
        if (total == 0)
            return null;
        return (long)(((Int128)bidQty - (Int128)askQty) * 10_000 / (Int128)total);
    }

    public (int Bids, int Asks) Depth() => (_bids.Count, _asks.Count);
}

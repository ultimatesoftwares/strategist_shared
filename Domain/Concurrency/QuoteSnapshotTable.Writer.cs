namespace Core.Concurrency;

/// <summary>Writer surface — each slot has EXACTLY ONE writing thread (the
/// venue dispatcher that registered it). Different slots may be written by
/// different threads concurrently; the same slot must never be.</summary>
public sealed partial class QuoteSnapshotTable
{
    /// <summary>
    /// Publishes a new top-of-book value into the slot. Seqlock write
    /// protocol: bump the sequence to odd (volatile — readers now retry),
    /// store the fields, bump to even (volatile — fields are published).
    /// Wait-free for the writer; readers mid-copy simply retry.
    /// </summary>
    public void Write(int slot, double bid, double ask, long tsMs, long seq)
    {
        ref Slot s = ref _slots[slot];

        // Odd sequence = "write in progress". The volatile store is a
        // release/acquire point: field stores below cannot move above it.
        Volatile.Write(ref s.Sequence, s.Sequence + 1);

        s.Bid = bid;
        s.Ask = ask;
        s.TsMs = tsMs;
        s.Seq = seq;

        // Back to even = "write complete". Release store: the field stores
        // above cannot move below it, so a reader that sees the even value
        // sees all of them.
        Volatile.Write(ref s.Sequence, s.Sequence + 1);
    }
}

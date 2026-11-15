namespace Core.Concurrency;

/// <summary>Reader surface — safe from ANY thread, any number of readers,
/// no locks, no allocation; readers can never block or slow the writer.</summary>
public sealed partial class QuoteSnapshotTable
{
    /// <summary>
    /// Copies the slot's current value. Returns false when nothing has been
    /// written yet (Seq is still 0). Seqlock read protocol: read the
    /// sequence (retry while odd = write in progress), copy the fields,
    /// fence, re-read the sequence — a change means a writer overlapped the
    /// copy, so retry. The loop converges because writes are rare relative
    /// to the copy cost (a handful of plain loads).
    /// </summary>
    public bool TryRead(int slot, out QuoteTick tick)
    {
        ref Slot s = ref _slots[slot];
        var spinner = new SpinWait();

        while (true)
        {
            int seq1 = Volatile.Read(ref s.Sequence);   // acquire: fields read below stay below
            if ((seq1 & 1) == 0)                        // even = stable; odd = writer mid-flight
            {
                tick = new QuoteTick
                {
                    VenueSlot = slot,
                    SymbolId = s.SymbolId,
                    Bid = s.Bid,
                    Ask = s.Ask,
                    TsMs = s.TsMs,
                    Seq = s.Seq,
                };

                // Full fence: the field loads above must complete before the
                // sequence is re-checked (a plain or acquire load would allow
                // the re-check to move ahead of the copies).
                Interlocked.MemoryBarrier();

                if (Volatile.Read(ref s.Sequence) == seq1)
                    return tick.Seq != 0;               // unwritten slot ⇒ false
            }
            spinner.SpinOnce();                         // writer mid-flight — retry shortly
        }
    }
}

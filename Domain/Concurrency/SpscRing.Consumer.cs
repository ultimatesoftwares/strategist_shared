using System.Diagnostics;

namespace Core.Concurrency;

/// <summary>Consumer surface — every member here may be called by the ONE
/// consumer thread only (enforced in DEBUG via a re-entrancy guard).</summary>
public sealed partial class SpscRing<T> where T : unmanaged
{
    /// <summary>Reads one item; false when the ring is empty.</summary>
    public bool TryRead(out T item)
    {
#if DEBUG
        Debug.Assert(Interlocked.CompareExchange(ref _consumerGuard, 1, 0) == 0,
            "SpscRing: concurrent consumers detected — the ring is single-consumer");
        try
        {
#endif
        long tail = _tail.Value;                       // consumer-owned: plain read
        if (Volatile.Read(ref _head.Value) == tail)    // acquire: see producer writes
        {
            item = default;
            return false;
        }
        item = _buffer[(int)(tail & _mask)];           // plain load (safe: slot is published)
        Volatile.Write(ref _tail.Value, tail + 1);     // release: slot may be recycled
        return true;
#if DEBUG
        }
        finally { Volatile.Write(ref _consumerGuard, 0); }
#endif
    }

    /// <summary>
    /// Reads up to <paramref name="destination"/>.Length items into the span
    /// and returns how many were copied. One volatile read of head and one
    /// volatile write of tail per batch — the cheap way to drain a burst.
    /// </summary>
    public int TryReadBatch(Span<T> destination)
    {
#if DEBUG
        Debug.Assert(Interlocked.CompareExchange(ref _consumerGuard, 1, 0) == 0,
            "SpscRing: concurrent consumers detected — the ring is single-consumer");
        try
        {
#endif
        long tail = _tail.Value;
        long head = Volatile.Read(ref _head.Value);
        int available = (int)(head - tail);
        if (available <= 0) return 0;

        int count = Math.Min(available, destination.Length);
        for (int i = 0; i < count; i++)
            destination[i] = _buffer[(int)((tail + i) & _mask)];
        Volatile.Write(ref _tail.Value, tail + count);
        return count;
#if DEBUG
        }
        finally { Volatile.Write(ref _consumerGuard, 0); }
#endif
    }

    /// <summary>
    /// Returns true exactly once per overflow episode: the producer dropped
    /// at least one item since the last call. The caller should resync its
    /// latest value from the <see cref="QuoteSnapshotTable"/> (the dropped
    /// items are gone; the snapshot always holds the newest value).
    /// </summary>
    public bool ConsumeGap() => Interlocked.Exchange(ref _gap, 0) == 1;
}

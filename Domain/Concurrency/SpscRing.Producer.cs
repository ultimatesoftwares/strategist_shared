using System.Diagnostics;

namespace Core.Concurrency;

/// <summary>Producer surface — every member here may be called by the ONE
/// producer thread only (enforced in DEBUG via a re-entrancy guard).</summary>
public sealed partial class SpscRing<T> where T : unmanaged
{
    /// <summary>
    /// Writes one item. Returns false (and drops THIS item, bumping
    /// <see cref="Dropped"/> and raising the gap flag) when the ring is
    /// full. Never blocks and never touches the consumer's tail index.
    /// </summary>
    public bool TryWrite(in T item)
    {
#if DEBUG
        Debug.Assert(Interlocked.CompareExchange(ref _producerGuard, 1, 0) == 0,
            "SpscRing: concurrent producers detected — the ring is single-producer");
        try
        {
#endif
        long head = _head.Value;                       // producer-owned: plain read
        long tail = Volatile.Read(ref _tail.Value);    // acquire: see consumer progress
        long fill = head - tail;

        if (fill >= _buffer.Length)
        {
            // Full: drop the NEW item. The consumer resyncs the latest value
            // from the snapshot table once it sees the gap flag, so dropping
            // new (rather than overwriting old) preserves in-ring ordering.
            _dropped.Value++;                          // producer-owned counter
            Volatile.Write(ref _gap, 1);
            return false;
        }

        if (fill + 1 > _highWater.Value) _highWater.Value = fill + 1;

        _buffer[(int)(head & _mask)] = item;           // plain store…
        Volatile.Write(ref _head.Value, head + 1);     // …published by release
        return true;
#if DEBUG
        }
        finally { Volatile.Write(ref _producerGuard, 0); }
#endif
    }
}

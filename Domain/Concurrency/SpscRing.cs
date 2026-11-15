using System.Runtime.InteropServices;

namespace Core.Concurrency;

/// <summary>A long padded to its own cache line. The 128-byte footprint with
/// the value in the middle pads both sides, so two PaddedLongs (or a
/// PaddedLong and its neighbours) can never share a cache line — no false
/// sharing between the producer-written and consumer-written indices.
/// (Top-level because explicit layout is not permitted inside a generic
/// type like <see cref="SpscRing{T}"/>.)</summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
    [FieldOffset(64)] public long Value;
}

/// <summary>
/// Single-producer / single-consumer ring buffer over a preallocated array
/// of unmanaged structs: ordered, allocation-free, lossless-or-flagged
/// delivery between exactly one producer thread and one consumer thread.
///
/// ── Ownership model ─────────────────────────────────────────────────────
/// <c>_head</c> (the next write index) is written ONLY by the producer;
/// <c>_tail</c> (the next read index) is written ONLY by the consumer. Each
/// side reads the other's index with <see cref="Volatile"/> semantics and
/// never writes it, so no CAS, lock, or fence beyond a volatile store is
/// needed anywhere. Indices grow monotonically and are masked into the
/// power-of-two buffer (<c>index &amp; _mask</c>), which also makes
/// full/empty unambiguous without wasting a slot:
/// empty ⇔ head == tail, full ⇔ head − tail == capacity.
///
/// ── Memory ordering ─────────────────────────────────────────────────────
/// Producer: write the slot with plain stores, then publish with
/// <c>Volatile.Write(_head)</c>. The volatile store has release semantics,
/// so a consumer that observes the new head (acquire via
/// <c>Volatile.Read</c>) is guaranteed to observe the completed slot write.
/// Consumer: <c>Volatile.Read(_head)</c>, copy the slot with plain loads,
/// then retire it with <c>Volatile.Write(_tail)</c> — the release store
/// guarantees the producer never recycles a slot the consumer is still
/// copying.
///
/// ── Overflow policy ─────────────────────────────────────────────────────
/// When full the producer DROPS THE NEW item (never blocks, never touches
/// <c>_tail</c>), increments <see cref="Dropped"/> and raises the gap flag.
/// The consumer observes the flag via <see cref="ConsumeGap"/> and is
/// expected to resync top-of-book from the <see cref="QuoteSnapshotTable"/>
/// — the ring then contains an ordered but stale prefix and the snapshot
/// holds the latest value, so nothing is ever processed out of order.
///
/// The class is split into partials by role:
///   SpscRing.cs          — state, construction, shared accessors
///   SpscRing.Producer.cs — TryWrite (producer-thread-only surface)
///   SpscRing.Consumer.cs — TryRead / TryReadBatch / ConsumeGap
/// </summary>
/// <typeparam name="T">Unmanaged element type (no references), so slot
/// copies are pure memmoves and the buffer is a flat allocation.</typeparam>
public sealed partial class SpscRing<T> where T : unmanaged
{
    private readonly T[] _buffer;
    private readonly int _mask;

    private PaddedLong _head;      // next write index — producer-owned
    private PaddedLong _tail;      // next read index  — consumer-owned
    private PaddedLong _dropped;   // items rejected while full — producer-owned
    private PaddedLong _highWater; // max observed fill level — producer-owned
    private int _gap;              // 1 = items were dropped since the consumer last resynced

#if DEBUG
    // The producer/consumer may legally migrate across thread-pool threads
    // (async continuations), so we cannot pin a thread id. Instead we assert
    // that the producer surface and the consumer surface are each never
    // entered CONCURRENTLY — which is the actual SPSC contract.
    private int _producerGuard;
    private int _consumerGuard;
#endif

    /// <param name="capacity">Ring capacity; rounded up to a power of two
    /// (minimum 2) so index masking replaces modulo on the hot path.</param>
    public SpscRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        int size = 2;
        while (size < capacity) size <<= 1;
        _buffer = new T[size];
        _mask = size - 1;
    }

    /// <summary>Ring capacity (a power of two).</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Items currently buffered. Safe to call from any thread, but
    /// the value is naturally a moment-in-time estimate.</summary>
    public int Count => (int)(Volatile.Read(ref _head.Value) - Volatile.Read(ref _tail.Value));

    /// <summary>Total items rejected because the ring was full.</summary>
    public long Dropped => Volatile.Read(ref _dropped.Value);

    /// <summary>Highest fill level the producer has observed — a cheap
    /// "how close to overflowing did we get" telemetry figure.</summary>
    public long HighWater => Volatile.Read(ref _highWater.Value);
}

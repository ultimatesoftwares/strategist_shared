using System.Runtime.InteropServices;

namespace Core.Concurrency;

/// <summary>
/// One top-of-book update on the engine's hot path. This is the ONLY payload
/// that travels through <see cref="SpscRing{T}"/> rings and
/// <see cref="QuoteSnapshotTable"/> slots — a fixed-size unmanaged struct so
/// the tick path never allocates, boxes, or touches a string.
///
/// Symbols and venues are referred to by interned integers:
/// <see cref="VenueSlot"/> is the snapshot-table slot index returned by
/// <see cref="QuoteSnapshotTable.Register"/> (the string venue key / symbol
/// name pair is interned exactly once, at session construction), and
/// <see cref="SymbolId"/> is the venue-side numeric symbol id.
///
/// <see cref="Seq"/> is the per-slot monotonic tick sequence assigned by the
/// producer (the venue dispatcher). Consumers use it to detect how far
/// behind a resynced snapshot they are: after a ring gap, a consumer reads
/// the snapshot slot and knows it is current when the snapshot's Seq is
/// greater than the last Seq it processed from the ring.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct QuoteTick
{
    /// <summary>Snapshot-table slot owning this (venue, symbol) pair.</summary>
    public int VenueSlot;

    /// <summary>Venue-side numeric symbol id (e.g. cTrader symbol id).</summary>
    public long SymbolId;

    /// <summary>Best bid.</summary>
    public double Bid;

    /// <summary>Best ask.</summary>
    public double Ask;

    /// <summary>Venue timestamp, Unix milliseconds.</summary>
    public long TsMs;

    /// <summary>Per-slot monotonic sequence number (1, 2, 3…) assigned by
    /// the single producer that owns the slot.</summary>
    public long Seq;

    /// <summary>Producer epoch — bumped when the venue connection behind the
    /// dispatcher is swapped (token rotation, reconnect). A consumer that
    /// sees it change logs the discontinuity and resyncs top-of-book from
    /// the snapshot table.</summary>
    public int Epoch;
}

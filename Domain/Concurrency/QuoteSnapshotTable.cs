using System.Runtime.InteropServices;

namespace Core.Concurrency;

/// <summary>
/// Lock-free latest-value table: the current top-of-book per
/// (venue, symbol), readable by ANY thread at ANY rate without ever building
/// a backlog — the complement of the ordered-but-bounded
/// <see cref="SpscRing{T}"/>. Heartbeats, PnL re-marks and the
/// engine.state.quote publishers read here; ring consumers resync here
/// after an overflow gap.
///
/// ── Interning ───────────────────────────────────────────────────────────
/// Strings never appear on the hot path. A venue session interns its
/// (venueKey, symbolId) pair exactly once at construction via
/// <see cref="Register"/> (lock-protected, startup only) and from then on
/// both writer and readers address the slot by the returned integer.
///
/// ── Seqlock protocol (one writer per slot) ──────────────────────────────
/// Each slot carries a sequence counter. The writer increments it to an ODD
/// value (write in progress), stores the fields with plain writes, then
/// increments it to the next EVEN value (write complete); the two counter
/// stores are volatile so they order the field stores between them. Readers
/// read the counter (retry while odd), copy the fields, fence, and re-read
/// the counter — if it changed, a write overlapped the copy and the reader
/// retries. Readers therefore never block the writer, never take a lock,
/// and never observe a torn bid/ask pair.
///
/// ── False sharing ───────────────────────────────────────────────────────
/// Slots are 128-byte aligned (<see cref="Slot"/> layout) so two venues'
/// writers never contend on the same cache line.
///
/// Split into partials by role:
///   QuoteSnapshotTable.cs        — state, interning, construction
///   QuoteSnapshotTable.Writer.cs — Write (slot-owner thread only)
///   QuoteSnapshotTable.Reader.cs — TryRead (any thread)
/// </summary>
public sealed partial class QuoteSnapshotTable
{
    /// <summary>One (venue, symbol) seqlock cell, padded to two cache lines
    /// so adjacent slots (different writer threads) never false-share.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Slot
    {
        [FieldOffset(0)] public int Sequence;  // odd = write in progress
        [FieldOffset(8)] public double Bid;
        [FieldOffset(16)] public double Ask;
        [FieldOffset(24)] public long TsMs;
        [FieldOffset(32)] public long Seq;     // producer's monotonic tick counter
        [FieldOffset(40)] public long SymbolId;
    }

    private readonly Slot[] _slots;
    private readonly Dictionary<(string VenueKey, long SymbolId), int> _index = new();
    private readonly object _registerLock = new();
    private int _used;

    /// <param name="capacity">Maximum number of distinct (venue, symbol)
    /// pairs the table can hold. Sized at startup; registration beyond it
    /// throws (it is a configuration error, not a runtime condition).</param>
    public QuoteSnapshotTable(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new Slot[capacity];
    }

    /// <summary>Distinct (venue, symbol) pairs registered so far.</summary>
    public int Registered => Volatile.Read(ref _used);

    /// <summary>
    /// Interns a (venue, symbol) pair and returns its slot index — the only
    /// place strings are touched, called once per venue session at
    /// construction (never on the tick path). Idempotent: re-registering an
    /// existing pair returns the same slot, so a restarted session keeps
    /// writing where its predecessor did.
    /// </summary>
    public int Register(string venueKey, long symbolId)
    {
        lock (_registerLock)
        {
            if (_index.TryGetValue((venueKey, symbolId), out int existing))
                return existing;
            if (_used >= _slots.Length)
                throw new InvalidOperationException(
                    $"QuoteSnapshotTable is full ({_slots.Length} slots) — raise the capacity");
            int slot = _used;
            _slots[slot].SymbolId = symbolId;
            _index[(venueKey, symbolId)] = slot;
            Volatile.Write(ref _used, _used + 1);
            return slot;
        }
    }
}

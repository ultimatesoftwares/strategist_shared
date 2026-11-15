namespace Core.Concurrency;

/// <summary>
/// The venue-edge funnel: ONE dispatcher per venue session turns raw quote
/// callbacks into (1) a snapshot-table write and (2) ring fan-out to every
/// subscribed consumer — in that order, always. Snapshot first, ring
/// second: a consumer that reacts to a ring item can therefore never read
/// an OLDER value from the snapshot table than the item it just processed.
///
/// The dispatcher is the single-producer rule made concrete: whatever
/// thread(s) the venue library calls back on, all quotes for a session pass
/// through <see cref="Dispatch"/>, which is the one writer for the
/// session's snapshot slot and the one producer for each attached ring.
/// (cTrader's client delivers callbacks sequentially, so no extra funnel is
/// needed today; a venue with concurrent callbacks — e.g. Alpaca's two
/// streaming clients — must serialize before calling Dispatch. The DEBUG
/// guards in <see cref="SpscRing{T}"/> catch violations.)
///
/// Split into partials by role:
///   QuoteDispatcher.cs        — state, construction, live Dispatch
///   QuoteDispatcher.Inline.cs — synchronous inline mode (backtests)
/// </summary>
public sealed partial class QuoteDispatcher
{
    /// <summary>A ring plus the waiter that wakes its consumer.</summary>
    public readonly record struct Subscription(SpscRing<QuoteTick> Ring, RingWaiter Waiter);

    private readonly QuoteSnapshotTable _table;
    private readonly int _slot;
    private readonly long _symbolId;
    private Subscription[] _subscriptions = [];
    private readonly object _subscribeLock = new();
    private long _seq; // producer-owned monotonic tick sequence for this slot
    private int _epoch; // bumped on connection swaps (rotation/reconnect)

    /// <param name="table">The engine-wide snapshot table.</param>
    /// <param name="venueKey">Stable venue identity (venue row GUID for live
    /// sessions, "sim:{instanceId}" for simulated feeds). Interned once,
    /// never touched again on the tick path.</param>
    /// <param name="symbolId">Venue-side numeric symbol id.</param>
    public QuoteDispatcher(QuoteSnapshotTable table, string venueKey, long symbolId)
    {
        _table = table;
        _symbolId = symbolId;
        _slot = table.Register(venueKey, symbolId);
    }

    /// <summary>The snapshot-table slot this dispatcher writes — hand it to
    /// latest-value readers (heartbeats, PnL marks, state publishers).</summary>
    public int Slot => _slot;

    /// <summary>Current producer epoch (see <see cref="QuoteTick.Epoch"/>).</summary>
    public int Epoch => Volatile.Read(ref _epoch);

    /// <summary>Marks a connection swap: every subsequent tick carries the
    /// new epoch so consumers detect the discontinuity and resync.</summary>
    public void BumpEpoch() => Interlocked.Increment(ref _epoch);

    /// <summary>Rings currently attached (registry refcounting: zero means
    /// the venue-side subscription can be dropped).</summary>
    public int SubscriberCount => Volatile.Read(ref _subscriptions).Length;

    /// <summary>
    /// Attaches a consumer ring. Called at instance start (not on the tick
    /// path); copy-on-write so <see cref="Dispatch"/> iterates a stable
    /// array without locking.
    /// </summary>
    public void Subscribe(SpscRing<QuoteTick> ring, RingWaiter waiter)
    {
        lock (_subscribeLock)
        {
            var next = new Subscription[_subscriptions.Length + 1];
            Array.Copy(_subscriptions, next, _subscriptions.Length);
            next[^1] = new Subscription(ring, waiter);
            Volatile.Write(ref _subscriptions, next);
        }
    }

    /// <summary>Detaches a consumer ring (instance stop).</summary>
    public void Unsubscribe(SpscRing<QuoteTick> ring)
    {
        lock (_subscribeLock)
        {
            Volatile.Write(ref _subscriptions,
                Array.FindAll(_subscriptions, s => !ReferenceEquals(s.Ring, ring)));
        }
    }

    /// <summary>
    /// The live tick path: assign the next sequence number, publish to the
    /// snapshot table (latest-value readers see it immediately), then fan
    /// out to every subscribed ring and wake its consumer. No allocation,
    /// no lock, no string.
    /// </summary>
    public void Dispatch(double bid, double ask, long tsMs)
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
            Epoch = Volatile.Read(ref _epoch),
        };

        var subscriptions = Volatile.Read(ref _subscriptions);
        for (int i = 0; i < subscriptions.Length; i++)
        {
            subscriptions[i].Ring.TryWrite(in tick);   // full ⇒ drop-new + gap flag
            subscriptions[i].Waiter.Signal();
        }
    }
}

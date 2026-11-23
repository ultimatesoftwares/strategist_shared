namespace Trading.Core;

public enum CrossVenueComparisonStatus
{
    Available,
    SessionUnavailable,
    FeedGap,
    UnmappedInstrument,
    QuoteUnavailable,
    StaleQuote,
    ArithmeticOverflow,
    ReferenceHistoryUnavailable,
    PreviousReferenceStale,
    NoReferenceMove,
    TargetUpdated,
}

public sealed record CrossVenueQuote(
    decimal Bid, decimal Ask, long ReceivedNanos, ulong SourceNanos);

/// <summary>
/// An indicative comparison, not a trading signal or a guarantee of available liquidity.
/// The caller supplies estimated costs in basis points of the buy price.
/// </summary>
public sealed record CrossVenueComparison(
    Guid InstrumentId, Guid BuyVenueId, Guid SellVenueId,
    CrossVenueQuote BuyQuote, CrossVenueQuote SellQuote,
    decimal GrossBps, decimal EstimatedCostBps, decimal EstimatedNetBps);

public sealed record CrossVenueLeadLag(
    Guid ReferenceVenueId, Guid TargetVenueId, string Direction,
    CrossVenueQuote PreviousReference, CrossVenueQuote CurrentReference,
    CrossVenueQuote CurrentTarget, CrossVenueComparison Cross);

/// <summary>
/// Single-owner, in-memory state for explicitly mapped atomic quote streams.
/// The owner must serialize all calls, assign a consecutive sequence to every
/// quote in a session, and report any upstream loss or disconnect. Receipt
/// times must use one monotonic nanosecond clock shared by all sessions.
/// This class does not subscribe to feeds or submit orders.
/// </summary>
public sealed class CrossVenueQuoteState
{
    private sealed class Session(Guid venueId, long priceScale, Dictionary<uint, Guid> mappings)
    {
        public Guid VenueId { get; } = venueId;
        public long PriceScale { get; } = priceScale;
        public Dictionary<uint, Guid> Mappings { get; } = mappings;
        public Dictionary<Guid, CrossVenueQuote> Quotes { get; } = [];
        public Dictionary<Guid, CrossVenueQuote> PreviousQuotes { get; } = [];
        public ulong NextSequence { get; set; }
        public long LastReceivedNanos { get; set; } = -1;
        public bool Gapped { get; set; }
    }

    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly Dictionary<Guid, Guid> _venueSessions = [];

    /// <summary>
    /// Replaces the current session for a venue, immediately invalidating its
    /// quotes. Mapping must be one-to-one within the session; reconnect or
    /// mapping changes require a new session.
    /// </summary>
    public Guid StartSession(Guid venueId, long priceScale, IReadOnlyDictionary<uint, Guid> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (venueId == Guid.Empty)
            throw new ArgumentException("A venue ID is required.", nameof(venueId));
        if (priceScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(priceScale));
        var copy = new Dictionary<uint, Guid>(mappings);
        if (copy.Values.Any(id => id == Guid.Empty) || copy.Values.Distinct().Count() != copy.Count)
            throw new ArgumentException("Mappings must identify a unique canonical instrument per symbol.", nameof(mappings));

        if (_venueSessions.Remove(venueId, out var previous))
            _sessions.Remove(previous);
        var sessionId = Guid.NewGuid();
        _sessions.Add(sessionId, new Session(venueId, priceScale, copy));
        _venueSessions.Add(venueId, sessionId);
        return sessionId;
    }

    public void EndSession(Guid sessionId)
    {
        if (_sessions.Remove(sessionId, out var session))
            _venueSessions.Remove(session.VenueId);
    }

    /// <summary>Marks all quotes in this session unusable until it is replaced.</summary>
    public void ReportGap(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new ArgumentException("Unknown session.", nameof(sessionId));
        session.Gapped = true;
        session.Quotes.Clear();
        session.PreviousQuotes.Clear();
    }

    /// <summary>
    /// Publish one quote; sequence starts at zero and includes invalid quotes.
    /// A sequence or clock regression poisons the whole session. An invalid
    /// quote clears the affected instrument until its next valid quote.
    /// </summary>
    public void Publish(Guid sessionId, ulong sequence, long receivedNanos, QuoteTick tick)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new ArgumentException("Unknown session.", nameof(sessionId));
        if (!session.Mappings.TryGetValue(tick.SymbolId, out var instrument))
        {
            ReportGap(sessionId);
            throw new ArgumentException("Quote symbol is not mapped in this session.", nameof(tick));
        }
        if (session.Gapped)
            throw new InvalidOperationException("The session must be replaced after a feed gap.");
        if (sequence != session.NextSequence || sequence == ulong.MaxValue
            || receivedNanos < 0 || receivedNanos < session.LastReceivedNanos)
        {
            ReportGap(sessionId);
            return;
        }
        session.NextSequence++;
        session.LastReceivedNanos = receivedNanos;
        if (tick.Bid <= 0 || tick.Ask <= 0 || tick.Bid > tick.Ask)
        {
            session.Quotes.Remove(instrument);
            session.PreviousQuotes.Remove(instrument);
            return;
        }
        if (session.Quotes.TryGetValue(instrument, out var previous))
            session.PreviousQuotes[instrument] = previous;
        session.Quotes[instrument] = new CrossVenueQuote(
            tick.Bid / (decimal)session.PriceScale, tick.Ask / (decimal)session.PriceScale,
            receivedNanos, tick.TsNanos);
    }

    /// <summary>
    /// Compares buying at one venue's ask with selling at the other's bid.
    /// Both sessions must be current, healthy, mapped and locally fresh.
    /// Source timestamps are retained as metadata, not trusted as local time.
    /// </summary>
    public CrossVenueComparisonStatus Compare(
        Guid buySessionId, Guid sellSessionId, Guid instrumentId, long nowNanos,
        long maxAgeNanos, decimal estimatedCostBps, out CrossVenueComparison? comparison)
    {
        comparison = null;
        if (instrumentId == Guid.Empty)
            throw new ArgumentException("An instrument ID is required.", nameof(instrumentId));
        if (nowNanos < 0)
            throw new ArgumentOutOfRangeException(nameof(nowNanos));
        if (maxAgeNanos <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAgeNanos));
        if (estimatedCostBps < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedCostBps));
        if (!_sessions.TryGetValue(buySessionId, out var buy)
            || !_sessions.TryGetValue(sellSessionId, out var sell) || buy.VenueId == sell.VenueId)
            return CrossVenueComparisonStatus.SessionUnavailable;
        if (buy.Gapped || sell.Gapped)
            return CrossVenueComparisonStatus.FeedGap;
        if (!buy.Mappings.ContainsValue(instrumentId) || !sell.Mappings.ContainsValue(instrumentId))
            return CrossVenueComparisonStatus.UnmappedInstrument;
        if (!buy.Quotes.TryGetValue(instrumentId, out var buyQuote)
            || !sell.Quotes.TryGetValue(instrumentId, out var sellQuote))
            return CrossVenueComparisonStatus.QuoteUnavailable;
        if (buyQuote.ReceivedNanos > nowNanos || sellQuote.ReceivedNanos > nowNanos
            || nowNanos - buyQuote.ReceivedNanos > maxAgeNanos
            || nowNanos - sellQuote.ReceivedNanos > maxAgeNanos)
            return CrossVenueComparisonStatus.StaleQuote;
        try
        {
            var gross = (sellQuote.Bid / buyQuote.Ask - 1m) * 10_000m;
            comparison = new CrossVenueComparison(instrumentId, buy.VenueId, sell.VenueId,
                buyQuote, sellQuote, gross, estimatedCostBps, gross - estimatedCostBps);
            return CrossVenueComparisonStatus.Available;
        }
        catch (OverflowException)
        {
            return CrossVenueComparisonStatus.ArithmeticOverflow;
        }
    }

    /// <summary>
    /// Observes a reference midpoint change while the target has received no
    /// quote since the previous reference quote. This is a local-arrival
    /// heuristic, not evidence of causal lead-lag or an executable signal.
    /// </summary>
    public CrossVenueComparisonStatus ObserveLeadLag(
        Guid referenceSessionId, Guid targetSessionId, Guid instrumentId, long nowNanos,
        long maxAgeNanos, decimal estimatedCostBps, out CrossVenueLeadLag? observation)
    {
        observation = null;
        var status = Compare(referenceSessionId, targetSessionId, instrumentId, nowNanos,
            maxAgeNanos, estimatedCostBps, out var initial);
        if (status != CrossVenueComparisonStatus.Available)
            return status;
        var reference = _sessions[referenceSessionId];
        if (!reference.PreviousQuotes.TryGetValue(instrumentId, out var previous))
            return CrossVenueComparisonStatus.ReferenceHistoryUnavailable;
        if (previous.ReceivedNanos > nowNanos || nowNanos - previous.ReceivedNanos > maxAgeNanos)
            return CrossVenueComparisonStatus.PreviousReferenceStale;
        var current = initial!.BuyQuote;
        var target = initial.SellQuote;
        if (target.ReceivedNanos >= previous.ReceivedNanos)
            return CrossVenueComparisonStatus.TargetUpdated;
        var previousMid = previous.Bid / 2m + previous.Ask / 2m;
        var currentMid = current.Bid / 2m + current.Ask / 2m;
        if (currentMid == previousMid)
            return CrossVenueComparisonStatus.NoReferenceMove;
        var rising = currentMid > previousMid;
        status = Compare(rising ? targetSessionId : referenceSessionId,
            rising ? referenceSessionId : targetSessionId, instrumentId, nowNanos,
            maxAgeNanos, estimatedCostBps, out var cross);
        if (status != CrossVenueComparisonStatus.Available)
            return status;
        observation = new CrossVenueLeadLag(reference.VenueId, _sessions[targetSessionId].VenueId,
            rising ? "up" : "down", previous, current, target, cross!);
        return CrossVenueComparisonStatus.Available;
    }
}

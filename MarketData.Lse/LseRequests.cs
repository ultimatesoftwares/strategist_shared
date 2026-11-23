namespace LSE.NET;

/// <summary>Catalogue filters, including exchange and options discovery. Paging is server-driven.</summary>
public sealed record LsePairSearch
{
    public string? Market { get; init; }
    public string? Symbol { get; init; }
    public string? Query { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
    public string? Sort { get; init; }
    public string? Cursor { get; init; }
    public string? GroupBy { get; init; }
    public string? Underlying { get; init; }
    public string? Product { get; init; }
    public DateOnly? Expiry { get; init; }
    public string? InstrumentType { get; init; }
    public string? OptionType { get; init; }
}

/// <summary>A wire selection for preview, estimate, and export. Use either two calendar dates
/// (inclusive) or a timestamp window (exclusive upper bound), not both.</summary>
public sealed record LseMarketSelection(string Market)
{
    public string? Symbol { get; init; }
    public long? PairId { get; init; }
    public string? Underlying { get; init; }
    public DateOnly? Expiry { get; init; }
    public string? OptionType { get; init; }
    public string? Data { get; init; }
    public string? Timeframe { get; init; }
    public LseDateRange? Window { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public bool? Adjusted { get; init; }
    public bool? AsPublished { get; init; }
    public string Format { get; init; } = "parquet";
}

/// <summary>Server-side features and filters for a candle dataset export.</summary>
public sealed record LseDatasetFeatures
{
    public IReadOnlyList<string>? Indicators { get; init; }
    public string? CompanionMarket { get; init; }
    public string? CompanionSymbol { get; init; }
    public long? CompanionPairId { get; init; }
    public IReadOnlyList<string>? CompanionFeatures { get; init; }
    public int? CorrelationWindow { get; init; }
    public int? HourStart { get; init; }
    public int? HourEnd { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyList<string>? DaysOfWeek { get; init; }
}

/// <summary>Filters the currently listed contracts of an option underlying.</summary>
public sealed record LseOptionChainQuery(string Underlying)
{
    public string? Type { get; init; }
    public DateOnly? Expiry { get; init; }
    public decimal? Strike { get; init; }
    public decimal? StrikeMin { get; init; }
    public decimal? StrikeMax { get; init; }
    public int? MinDaysToExpiry { get; init; }
    public int? MaxDaysToExpiry { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
}

/// <summary>Filters option prints; use either inclusive calendar dates or a half-open timestamp window.</summary>
public sealed record LseOptionFlowQuery
{
    public string? Underlying { get; init; }
    public decimal? MinPremium { get; init; }
    public string? Type { get; init; }
    public DateOnly? Expiry { get; init; }
    public decimal? Strike { get; init; }
    public decimal? StrikeMin { get; init; }
    public decimal? StrikeMax { get; init; }
    public int? MinDaysToExpiry { get; init; }
    public int? MaxDaysToExpiry { get; init; }
    public LseDateRange? Window { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Order { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
}

/// <summary>Filters the economic series catalogue. Without a kind, lists kinds and countries.</summary>
public sealed record LseMacroSearch
{
    public string? Kind { get; init; }
    public string? Country { get; init; }
    public string? Code { get; init; }
    public string? Query { get; init; }
    public string? Group { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
}

/// <summary>One macro series and optional inclusive calendar bounds. Kinds: observation, yield, release.</summary>
public sealed record LseMacroSelection(string Code, string Kind = "observation",
    DateOnly? From = null, DateOnly? To = null);

/// <summary>Filters a reference collection by series, country, text, and inclusive calendar bounds.</summary>
public sealed record LseReferenceSelection(string Dataset, string? Symbol = null, string? Country = null,
    string? Query = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>A vendor instrument image and its cache metadata.</summary>
public sealed record LseInstrumentImage(byte[] Bytes, string? ContentType, string? ETag, string? CacheControl);

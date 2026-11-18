namespace MarketData.Lse;

/// <summary>
/// Settings of the LSE Data 3.0 client (londonstrategicedge.com). The
/// defaults mirror the vendor's own Python client: 60 s per call, 3 retries
/// on no-answer/429/500/502/503/504, and honouring a short Retry-After once
/// per call.
/// </summary>
public sealed class LseOptions
{
    /// <summary>The key from the vendor's Data page, sent as X-API-Key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The LSE Data 3.0 server.</summary>
    public string BaseUrl { get; set; } = "https://download.londonstrategicedge.com";

    /// <summary>Seconds to wait for each HTTP call to start answering.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Retries of a call that got no answer, or a 429/500/502/503/504.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>On a 429 with a Retry-After of two minutes or less, wait it
    /// out once per call without using up a retry.</summary>
    public bool WaitForLimit { get; set; } = true;

    /// <summary>The plan's calls a minute, enforced client-side so a
    /// backfill never trips 429 calls-exhausted. 60 on the free plan.</summary>
    public int CallsPerMinute { get; set; } = 60;

    /// <summary>File format of tick exports: "parquet" (default, smallest)
    /// or "csv".</summary>
    public string TickFormat { get; set; } = "parquet";

    /// <summary>Adjust prices and volumes for splits (the server default).</summary>
    public bool Adjusted { get; set; } = true;
}

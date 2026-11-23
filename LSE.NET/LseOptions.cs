namespace LSE.NET;

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

    /// <summary>Per-attempt time limit for the HTTP headers or an individual body read.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Retries of a transient failure, including an interrupted export or bad checksum.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>On a 429 or 503 with a Retry-After of two minutes or less, wait it
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

    /// <summary>Maximum buffered export size. Both file bytes and typed rows may be held in memory.</summary>
    public int MaxExportBytes { get; set; } = 128 * 1024 * 1024;

    /// <summary>Maximum buffered successful JSON response size.</summary>
    public int MaxJsonBytes { get; set; } = 8 * 1024 * 1024;

    internal static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("Supply an LSE API key without whitespace or control characters.", "apiKey");
    }

    internal static bool IsSecureEndpoint(Uri uri, string secure, string insecure) =>
        uri.IsAbsoluteUri && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri.Scheme == secure || uri.IsLoopback && uri.Scheme == insecure);
}

using System.Globalization;
using System.Text.Json;
using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

/// <summary>
/// QuestDB access for any service hosting the Repository module: the
/// historical market-data store (writes via ILP over HTTP, reads via /exec
/// SQL, fetches from a registered venue — ctrader or metatrader).
/// Configure via "QuestDb:Url" (QuestDb__Url in the compose
/// environment) or the QUESTDB_URL environment variable; defaults to the
/// compose service. Tables are created WAL + DEDUP on first write, so
/// re-provisioning a range upserts instead of duplicating.
/// Hosts register it as a singleton (the Adapters assembly's
/// RepositoryHost.AddRepository, or directly in the fleet hub).
///
/// Partial class layout (one concern per file, like the engine venues):
/// QuestDb.cs (state/ctor/helpers), Ranges.cs (calendar splitting),
/// Fetch.cs (venue sources), Writes.cs (ILP), Reads.cs (SQL),
/// Schema.cs (table setup), Types.cs (records + source interface).
/// </summary>
public sealed partial class QuestDb : IDisposable
{
    private readonly string _baseUrl;
    private readonly HttpClient _http;
    private readonly ILogger<QuestDb> _logger;
    private readonly TimeSpan _fetchThrottle;
    private readonly TimeSpan _retryBackoff;
    private readonly Dictionary<string, IVenueHistorySource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private bool _tablesEnsured;

    public QuestDb(IConfiguration configuration, ILogger<QuestDb> logger,
        HttpClient? http = null)
    {
        _baseUrl = (configuration["QuestDb:Url"]
            ?? Environment.GetEnvironmentVariable("QUESTDB_URL")
            ?? "http://questdb:9000").TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _logger = logger;
        // Pause between venue microtasks so long jobs never trip rate limits.
        _fetchThrottle = TimeSpan.FromMilliseconds(
            double.TryParse(configuration["QuestDb:FetchThrottleMs"],
                NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ? ms : 500);
        // Base backoff before retrying a failed microtask (scaled by attempt).
        _retryBackoff = TimeSpan.FromMilliseconds(
            double.TryParse(configuration["QuestDb:FetchRetryBackoffMs"],
                NumberStyles.Float, CultureInfo.InvariantCulture, out var backoff) ? backoff : 2000);
    }

    // ---- helpers ----------------------------------------------------------------

    private static string Iso(DateTime ts) => AsUtc(ts).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
    private static string Escape(string s) => s.Replace("'", "''");
    private static DateTime Ts(JsonElement cell) =>
        DateTime.Parse(cell.GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public void Dispose() => _http.Dispose();
}

using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

// Schema: WAL tables with DEDUP upsert keys, created before the first write.
public sealed partial class QuestDb
{
    /// <summary>Pre-creates bars/ticks as WAL tables with DEDUP upsert keys so
    /// re-provisioned ranges update in place. Idempotent; once per instance.</summary>
    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        if (_tablesEnsured) return;
        string[] statements =
        [
            "CREATE TABLE IF NOT EXISTS bars (symbol SYMBOL, tf SYMBOL, open DOUBLE, high DOUBLE, " +
            "low DOUBLE, close DOUBLE, volume DOUBLE, ts TIMESTAMP) TIMESTAMP(ts) PARTITION BY DAY WAL " +
            "DEDUP UPSERT KEYS(ts, symbol, tf)",
            "CREATE TABLE IF NOT EXISTS ticks (symbol SYMBOL, venue SYMBOL, bid DOUBLE, ask DOUBLE, " +
            "last DOUBLE, last_size DOUBLE, seq LONG, ts TIMESTAMP) TIMESTAMP(ts) PARTITION BY DAY WAL " +
            "DEDUP UPSERT KEYS(ts, symbol, venue)",
        ];
        foreach (var sql in statements)
        {
            using var response = await _http.GetAsync(
                $"{_baseUrl}/exec?query={Uri.EscapeDataString(sql)}", ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("QuestDB table setup failed ({Sql}): {Detail}",
                    sql, await response.Content.ReadAsStringAsync(ct));
        }
        _tablesEnsured = true;
    }
}

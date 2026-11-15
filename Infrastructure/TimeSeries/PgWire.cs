using Npgsql;

using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

/// <summary>
/// QuestDB reads over PGWire (port 8812) with Npgsql — replacing the
/// hand-rolled /exec HTTP JSON round-trips (and their observed keep-alive
/// response corruption; see the git history of QuestDbExec.cs). Writes stay
/// on ILP (<see cref="QuestDb"/>); this is a read-only query surface.
///
/// Npgsql is an implementation detail of this adapter: consumers open a
/// session with <see cref="OpenAsync"/> and never see a connection type.
///
/// Connection resolution: "QuestDb:PgWire" (a full Npgsql connection string)
/// wins when set; otherwise the host is derived from "QuestDb:Url" /
/// QUESTDB_URL (the HTTP endpoint every deployment already configures) with
/// QuestDB's PGWire defaults (admin/quest/qdb). NoTypeLoading is required —
/// QuestDB does not implement the pg_catalog queries Npgsql's type loader
/// issues.
/// </summary>
public sealed class QuestDbPgReader : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;

    private QuestDbPgReader(NpgsqlConnection connection) => _connection = connection;

    /// <summary>Opens a PGWire session against the configured store. Dispose
    /// after the read (connections are pooled by Npgsql underneath).</summary>
    public static async Task<QuestDbPgReader> OpenAsync(
        IConfiguration configuration, CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(ConnectionString(configuration));
        try
        {
            await connection.OpenAsync(ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return new QuestDbPgReader(connection);
    }

    public static string ConnectionString(IConfiguration configuration)
    {
        if (configuration["QuestDb:PgWire"] is { Length: > 0 } explicitCs)
            return explicitCs;
        var url = configuration["QuestDb:Url"]
            ?? Environment.GetEnvironmentVariable("QUESTDB_URL")
            ?? "http://localhost:9000";
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "localhost";
        // CS0618: the connection-string form is obsolete in favour of
        // NpgsqlDataSourceBuilder, but these one-shot readers open plain
        // NpgsqlConnections (no pooled data source) and QuestDB requires
        // NoTypeLoading — the option remains functional on this path.
#pragma warning disable CS0618
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = 8812,
            Username = "admin",
            Password = "quest",
            Database = "qdb",
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            Timeout = 15,
            CommandTimeout = 120,
        }.ConnectionString;
#pragma warning restore CS0618
    }

    /// <summary>"table does not exist" is a normal empty store (nothing
    /// provisioned yet), never an error — the PGWire analogue of the old
    /// reader's non-2xx-means-absent convention.</summary>
    internal static bool IsMissingTable(PostgresException ex) =>
        ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    /// <summary>The designated timestamp column of a table ("ts" when the
    /// table is absent or unnamed) — the ILP writer named it "timestamp" in
    /// older stores, "ts" in new ones; never assume.</summary>
    public async Task<string> DesignatedTimestampAsync(string table, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            $"select designatedTimestamp from tables() where table_name = '{table.Replace("'", "''")}'";
        try
        {
            return await cmd.ExecuteScalarAsync(ct) as string ?? "ts";
        }
        catch (PostgresException ex) when (IsMissingTable(ex))
        {
            return "ts";
        }
    }

    /// <summary>Rows of a "SELECT {ts}, …doubles" query: the designated
    /// timestamp (UTC) followed by <paramref name="cells"/> doubles. Null
    /// when the table does not exist yet.</summary>
    public async Task<List<(DateTime Ts, double[] Cells)>?> QueryTsRowsAsync(
        string sql, int cells, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        try
        {
            var rows = new List<(DateTime, double[])>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // QuestDB timestamps are UTC; PGWire carries them as
                // "timestamp without time zone", so the kind must be pinned.
                var ts = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
                var values = new double[cells];
                for (var i = 0; i < cells; i++)
                    values[i] = reader.IsDBNull(i + 1) ? 0 : reader.GetDouble(i + 1);
                rows.Add((ts, values));
            }
            return rows;
        }
        catch (PostgresException ex) when (IsMissingTable(ex))
        {
            return null;
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

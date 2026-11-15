using QuestDB;
using QuestDB.Senders;

using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

// Writes: ILP over HTTP into the bars/ticks tables (dedup-upserted), sent
// with the official net-questdb-client sender instead of hand-built lines.
public sealed partial class QuestDb
{
    private string? _senderConf;

    /// <summary>One ILP sender per write call, configured off the same HTTP
    /// endpoint as the rest of the adapter. auto_flush stays off — each write
    /// call batches its rows and flushes exactly once, preserving the old
    /// one-POST-per-chunk behaviour the fetch jobs rely on.</summary>
    private ISender NewSender()
    {
        if (_senderConf is null)
        {
            var uri = new Uri(_baseUrl);
            var protocol = uri.Scheme == Uri.UriSchemeHttps ? "https" : "http";
            _senderConf = $"{protocol}::addr={uri.Host}:{uri.Port};auto_flush=off;";
        }
        return Sender.New(_senderConf);
    }

    /// <summary>Writes bars into the historical store (upsert on ts+symbol+tf).</summary>
    public async Task<int> WriteBarsAsync(IEnumerable<Bar> bars, CancellationToken ct = default)
    {
        await EnsureTablesAsync(ct);
        using var sender = NewSender();
        var count = 0;
        foreach (var b in bars)
        {
            sender.Table("bars")
                .Symbol("symbol", b.Symbol)
                .Symbol("tf", b.Timeframe)
                .Column("open", b.Open)
                .Column("high", b.High)
                .Column("low", b.Low)
                .Column("close", b.Close)
                .Column("volume", b.Volume);
            await sender.AtAsync(MillisUtc(b.Ts), ct);
            count++;
        }
        if (count > 0) await sender.SendAsync(ct);
        return count;
    }

    /// <summary>Writes ticks into the historical store (upsert on ts+symbol+venue).</summary>
    public async Task<int> WriteTicksAsync(IEnumerable<Tick> ticks, CancellationToken ct = default)
    {
        await EnsureTablesAsync(ct);
        using var sender = NewSender();
        var count = 0;
        foreach (var t in ticks)
        {
            sender.Table("ticks")
                .Symbol("symbol", t.Symbol)
                .Symbol("venue", t.Venue)
                .Column("bid", t.Bid)
                .Column("ask", t.Ask)
                .Column("last", t.Last)
                .Column("last_size", t.LastSize)
                .Column("seq", t.Seq);
            await sender.AtAsync(MillisUtc(t.Ts), ct);
            count++;
        }
        if (count > 0) await sender.SendAsync(ct);
        return count;
    }

    /// <summary>Millisecond-truncated UTC timestamp: the hand-rolled ILP
    /// writer always sent ms-precision nanos, and the DEDUP upsert keys
    /// include ts — finer precision would stop re-provisioned rows from
    /// upserting over rows written before the client swap.</summary>
    private static DateTime MillisUtc(DateTime ts)
    {
        var utc = AsUtc(ts);
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}

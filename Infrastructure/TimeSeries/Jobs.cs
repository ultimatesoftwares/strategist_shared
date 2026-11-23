using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

/// <summary>Published state of one fetch job: raised in-process after every
/// microtask so hosts can live-track long provisioning runs.</summary>
public sealed record FetchJobState(
    string JobId, string Kind, string Venue, string Symbol, string? Timeframe,
    DateTime From, DateTime To,
    int TotalChunks, int CompletedChunks, int FailedChunks, long RowsWritten,
    string Status, DateTime? ChunkFrom, DateTime? ChunkTo, DateTime UpdatedAt)
{
    public string? LastChunkError { get; init; }
}

/// <summary>Outcome of a fetch job. Failed chunks never abort the job: they
/// are collected here so the caller can retry just the holes.</summary>
public sealed record FetchJobResult(
    string JobId, long Written, IReadOnlyList<DateRange> FailedRanges)
{
    public bool Complete => FailedRanges.Count == 0;
}

// Fetch jobs: a long range (e.g. 2018-02-03..2026-06-04) is split into
// calendar microtasks, each retried independently and throttled between
// chunks so venue rate limits are never tripped; one bad chunk is recorded
// and skipped, not fatal. Job state is raised in-process as it moves.
public sealed partial class QuestDb
{
    /// <summary>Raised on every state change — in-process observers
    /// (ProvisionRunner) and tests hook here.</summary>
    public event Action<FetchJobState>? JobStateChanged;

    private const int MaxAttempts = 3;

    private async Task<FetchJobResult> RunFetchJobAsync(
        string kind, string venue, string symbol, string? timeframe,
        DateRange range, RangeUnit chunkUnit, string? callerJobId,
        Func<DateRange, CancellationToken, Task<int>> microtask,
        CancellationToken ct, Func<Exception, bool>? stopOnError = null)
    {
        // Callers that track the job elsewhere (e.g. the backtest slave's
        // provision mirror) supply their own id so states correlate.
        var jobId = callerJobId ?? Guid.NewGuid().ToString("N");
        var chunks = SplitRange(range, chunkUnit);
        var written = 0L;
        var completed = 0;
        var failed = new List<DateRange>();
        string? lastChunkError = null;

        FetchJobState Snapshot(string status, DateRange? current = null) => new(
            jobId, kind, venue, symbol, timeframe, range.From, range.To,
            chunks.Count, completed, failed.Count, written, status,
            current?.From, current?.To, DateTime.UtcNow)
        {
            LastChunkError = lastChunkError,
        };

        await PublishJobStateAsync(Snapshot("running"), ct);

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var done = false;
            for (var attempt = 1; attempt <= MaxAttempts && !done; attempt++)
            {
                try
                {
                    written += await microtask(chunk, ct);
                    done = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    await PublishJobStateAsync(Snapshot("cancelled", chunk), CancellationToken.None);
                    throw;
                }
                catch (Exception ex) when (stopOnError?.Invoke(ex) == true)
                {
                    // This chunk was not processed; leave the last published checkpoint intact.
                    throw;
                }
                catch (Exception ex) when (attempt < MaxAttempts)
                {
                    _logger.LogWarning(ex,
                        "questdb job {JobId}: chunk {Chunk} failed (attempt {Attempt}/{Max}), retrying",
                        jobId, chunk, attempt, MaxAttempts);
                    await Task.Delay(_retryBackoff * attempt, ct);
                }
                catch (Exception ex)
                {
                    // Fault tolerance: record the hole and move on — the rest
                    // of the range still lands; the caller can retry the holes.
                    failed.Add(chunk);
                    lastChunkError = ex.Message;
                    _logger.LogError(ex,
                        "questdb job {JobId}: chunk {Chunk} failed {Max} times, skipping",
                        jobId, chunk, MaxAttempts);
                }
            }
            if (done) completed++;

            await PublishJobStateAsync(Snapshot("running", chunk), ct);

            // Rate-limit guard: breathe between venue calls.
            if (_fetchThrottle > TimeSpan.Zero && index < chunks.Count - 1)
                await Task.Delay(_fetchThrottle, ct);
        }

        var final = failed.Count == 0 ? "completed" : "partial";
        await PublishJobStateAsync(Snapshot(final), ct);
        _logger.LogInformation(
            "questdb job {JobId}: {Status} — {Written} rows, {Completed}/{Total} chunks ({Failed} failed)",
            jobId, final, written, completed, chunks.Count, failed.Count);
        return new FetchJobResult(jobId, written, failed);
    }

    /// <summary>Raises the in-process job-state event (the old best-effort
    /// RabbitMQ mirror on "historical.jobs" had no consumers and was retired
    /// with the bus).</summary>
    private Task PublishJobStateAsync(FetchJobState state, CancellationToken ct)
    {
        JobStateChanged?.Invoke(state);
        return Task.CompletedTask;
    }
}

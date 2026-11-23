using System.Diagnostics;

namespace Trading.Core;

/// <summary>Fixed, cumulative duration histogram; samples are Stopwatch ticks.</summary>
public sealed class LatencyHistogram
{
    private static readonly double[] BoundsMs = [.1, .5, 1, 5, 10, 50, 100, 500];
    private readonly long[] _buckets = new long[BoundsMs.Length + 1];
    private long _count;
    private long _totalTicks;
    private long _maxTicks;

    public void Observe(long elapsedTicks)
    {
        var milliseconds = elapsedTicks * 1000d / Stopwatch.Frequency;
        var bucket = 0;
        while (bucket < BoundsMs.Length && milliseconds > BoundsMs[bucket])
            bucket++;
        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Add(ref _totalTicks, elapsedTicks);
        Interlocked.Increment(ref _count);
        long previous;
        while (elapsedTicks > (previous = Interlocked.Read(ref _maxTicks))
            && Interlocked.CompareExchange(ref _maxTicks, elapsedTicks, previous) != previous) { }
    }

    public LatencySnapshot Snapshot()
    {
        var buckets = new LatencyBucket[_buckets.Length];
        long cumulative = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            cumulative += Interlocked.Read(ref _buckets[i]);
            buckets[i] = new LatencyBucket(i < BoundsMs.Length ? BoundsMs[i] : null, cumulative);
        }
        var count = Interlocked.Read(ref _count);
        var total = Interlocked.Read(ref _totalTicks);
        var max = Interlocked.Read(ref _maxTicks);
        return new LatencySnapshot(count, count == 0 ? null : total * 1000d / Stopwatch.Frequency / count,
            count == 0 ? null : max * 1000d / Stopwatch.Frequency, buckets);
    }
}

/// <summary>Snapshot values are approximate while events are being processed.</summary>
public sealed record LatencySnapshot(long Count, double? MeanMs, double? MaxMs, LatencyBucket[] Buckets);

/// <summary>Cumulative samples at or below UpperBoundMs; null means all samples.</summary>
public sealed record LatencyBucket(double? UpperBoundMs, long Count);

/// <summary>Locally measured durations; excludes upstream market-feed latency.</summary>
public sealed record EngineLatencySnapshot(
    LatencySnapshot MarketQueue, LatencySnapshot MarketProcessing, LatencySnapshot BrokerSubmission,
    long ExpiredQuotes, LatencySnapshot BrokerFirstResponse, LatencySnapshot BrokerFirstFill,
    long OrderTimingEvictions, bool SyntheticExecutions,
    LatencySnapshot QuoteToSubmission, LatencySnapshot QuoteToFirstResponse, LatencySnapshot QuoteToFirstFill);

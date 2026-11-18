namespace MarketData.Lse;

/// <summary>
/// A sliding-window rate limiter: at most <c>limit</c> acquisitions per
/// window, waiting out the excess. Keeps a backfill under the plan's
/// calls-a-minute budget instead of burning retries on 429s.
/// </summary>
internal sealed class RateGate(int limit, TimeSpan window)
{
    private readonly Queue<DateTime> _stamps = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan wait;
            await _lock.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                while (_stamps.Count > 0 && now - _stamps.Peek() >= window)
                    _stamps.Dequeue();
                if (_stamps.Count < limit)
                {
                    _stamps.Enqueue(now);
                    return;
                }
                wait = _stamps.Peek() + window - now;
            }
            finally
            {
                _lock.Release();
            }
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(10), ct);
        }
    }
}

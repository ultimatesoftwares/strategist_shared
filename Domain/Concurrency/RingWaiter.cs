namespace Core.Concurrency;

/// <summary>
/// Consumer wake-up helper for a <see cref="SpscRing{T}"/>: bounded spin,
/// then park on a semaphore until the producer signals. Kept OUTSIDE the
/// ring on purpose — the ring stays a pure data structure; blocking policy
/// is the consumer loop's business.
///
/// Contract: the consumer calls <see cref="Wait"/> only after finding the
/// ring empty, passing a <c>hasWork</c> probe; the producer calls
/// <see cref="Signal"/> after every publish (cheap: one interlocked
/// exchange, a release only when a consumer is actually parked). Spurious
/// wake-ups are allowed and expected — the consumer loop must simply
/// re-check the ring after Wait returns.
///
/// The arm/double-check dance closes the classic lost-wakeup race:
///   consumer: sees empty → arms (_state = 1) → re-checks → parks
///   producer: publishes → Signal (exchange _state 0; release if it was 1)
/// If the producer publishes between the consumer's empty-check and its
/// arm, the re-check after arming finds the item. If it publishes after the
/// re-check, the exchange sees _state == 1 and releases the semaphore, so
/// the park returns immediately.
/// </summary>
public sealed class RingWaiter
{
    private readonly SemaphoreSlim _signal = new(0);
    private int _state; // 0 = consumer running, 1 = consumer armed/parked

    /// <summary>Producer side: wakes the consumer if (and only if) it is
    /// armed or parked. Called after every publish; near-free when the
    /// consumer is busy draining.</summary>
    public void Signal()
    {
        if (Volatile.Read(ref _state) == 1 && Interlocked.Exchange(ref _state, 0) == 1)
            _signal.Release();
    }

    /// <summary>
    /// Consumer side: spins briefly, then parks until signalled or
    /// cancelled. Returns as soon as <paramref name="hasWork"/> turns true
    /// during the spin phase, when signalled, or spuriously — the caller
    /// re-checks the ring either way.
    /// </summary>
    /// <param name="hasWork">Cheap probe (e.g. <c>ring.Count &gt; 0</c>).
    /// Cache the delegate at the call site — one allocation per loop, none
    /// per wait.</param>
    /// <param name="spinIterations">Spin budget before parking; keep small
    /// (the default ~64 covers a producer mid-publish).</param>
    public void Wait(Func<bool> hasWork, CancellationToken ct, int spinIterations = 64)
    {
        var spinner = new SpinWait();
        for (int i = 0; i < spinIterations; i++)
        {
            if (hasWork()) return;
            spinner.SpinOnce();
        }

        Volatile.Write(ref _state, 1);     // arm
        if (hasWork())                     // double-check: close the race
        {
            Interlocked.Exchange(ref _state, 0);
            return;                        // a stale permit only causes a spurious wake
        }
        _signal.Wait(ct);                  // park until Signal() or cancellation
    }
}

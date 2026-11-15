namespace Core.Strategy;

/// <summary>
/// The strategy-evaluation port: one running strategy instance, whatever
/// evaluates it — the native quant package runtime
/// (<c>Engine.Quant.QuantStrategyEvaluator</c>). Hosts (live instance)
/// depend on this surface only, so an evaluator cutover touches no
/// callers.
///
/// Threading contract: single-threaded. Every member except
/// <see cref="Warming"/> must be called from the one thread that drives
/// <see cref="OnTick"/>; <see cref="Warming"/> tolerates benign cross-thread
/// reads (heartbeat).
/// </summary>
public interface IStrategyEvaluator
{
    /// <summary>The strategy's name.</summary>
    string Name { get; }

    /// <summary>The design symbol the strategy evaluates.</summary>
    string Symbol { get; }

    /// <summary>True until every indicator has produced a real value —
    /// the strategy cannot signal while warming (GUI "warmup" badge).</summary>
    bool Warming { get; }

    /// <summary>Subscribes the strategy's symbol and announces the start.</summary>
    void Start(IStrategyHost host);

    /// <summary>Closes any open position and detaches the host.</summary>
    void Stop();

    /// <summary>Feeds one quote. Returns true when the strategy declares
    /// itself done (never, currently — kept for replay-driver symmetry).</summary>
    bool OnTick(string symbol, double bid, double ask, long timestampMs);

    /// <summary>Seeds historical CLOSED bars through the indicator pipeline
    /// without firing actions. Call once, before the first
    /// <see cref="OnTick"/>, with bars in ascending time order; bar mode
    /// only. Returns the bars consumed.</summary>
    int SeedHistory(
        IEnumerable<(long StartUnixSeconds, double Open, double High, double Low, double Close, double Volume)> bars);

    /// <summary>Restores an open position recovered during reconciliation
    /// (engine spec §9: adopt, don't close). Must run before the first
    /// <see cref="OnTick"/>.</summary>
    void AdoptPosition(long positionId, int direction, double entryPrice);

    /// <summary>The host observed the venue position closed externally;
    /// resets position state WITHOUT sending a close order.</summary>
    void OnExternalFlat();
}

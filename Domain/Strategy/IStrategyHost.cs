namespace Core.Strategy;

/// <summary>
/// What a running strategy asks of its host: the engine agent routes these
/// to the attached venue; the backtest worker routes them to the replay's
/// simulated position. All calls arrive on the single thread that drives
/// the strategy.
/// </summary>
public interface IStrategyHost
{
    void Subscribe(string symbol);

    /// <summary>Open a long position; returns the position id or -1.</summary>
    long Buy(string symbol, double volumeLots);

    /// <summary>Open a short position; returns the position id or -1.</summary>
    long Sell(string symbol, double volumeLots);

    /// <summary>Close a position; 0 on success.</summary>
    int Close(long positionId);

    /// <summary>Strategy log line (0 info, 1 warn, 2 error).</summary>
    void Log(int level, string message);
}

namespace FleetBridge;

/// <summary>
/// Connector configuration captured from Fleet_Init.
/// </summary>
internal sealed record BridgeConfig
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string TerminalId { get; init; }
    public int Platform { get; init; }
    public string TerminalBuild { get; init; } = "";
    public long AccountLogin { get; init; }
    public string BrokerServer { get; init; } = "";
    public int ServerTzOffsetMinutes { get; init; }
    public int MarginMode { get; init; }
    public string BrokerName { get; init; } = "";
}

/// <summary>
/// Spike-grade shared state behind the FFI surface.
///
/// What is real: lifecycle (Init/Shutdown), state reporting, last-error
/// tracking, the capped EA log queue, and a background worker thread — enough
/// to exercise NativeAOT threads + GC under Wine (feasibility gate 1).
///
/// What is deliberately stubbed: the FIX transport. It is wired up only after
/// tools/FixSmoke proves QuickFIX/n works under NativeAOT (gate 2). Until
/// then the connector stays in STATE_CONNECTING and data-path calls are
/// accepted and dropped.
/// </summary>
internal sealed class SharedState
{
    private static readonly object GlobalMu = new();
    private static SharedState? _instance;
    private static string _globalError = "";

    private readonly object _mu = new();
    private readonly Queue<string> _logs = new();
    private readonly Thread _worker;
    private readonly ManualResetEventSlim _stop = new(false);
    private int _state = Consts.StateConnecting;
    private string _lastError = "";
    private long _heartbeatIntervalMs = Consts.DefaultHeartbeatMs;

    public BridgeConfig Config { get; }

    private SharedState(BridgeConfig config)
    {
        Config = config;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "fleet-bridge-worker",
        };
        _worker.Start();
    }

    // ---- global lifecycle -------------------------------------------------

    /// <summary>Replaces any existing instance (EA re-attach restarts cleanly).</summary>
    public static void Start(BridgeConfig config)
    {
        lock (GlobalMu)
        {
            _instance?.StopCore();
            _instance = new SharedState(config);
            _globalError = "";
        }
    }

    public static int Shutdown()
    {
        lock (GlobalMu)
        {
            _instance?.StopCore();
            _instance = null;
            return 0;
        }
    }

    /// <summary>
    /// Runs <paramref name="fn"/> against the live instance, or returns
    /// <paramref name="fallback"/> when the bridge is not initialized —
    /// mirrors with_shared() in the C++ bridge.
    /// </summary>
    public static T WithShared<T>(T fallback, Func<SharedState, T> fn)
    {
        SharedState? s;
        lock (GlobalMu)
        {
            s = _instance;
        }
        return s is null ? fallback : fn(s);
    }

    public static void SetGlobalError(string message)
    {
        lock (GlobalMu)
        {
            _globalError = message;
        }
    }

    public static string GlobalError()
    {
        lock (GlobalMu)
        {
            return _globalError;
        }
    }

    // ---- instance state ---------------------------------------------------

    public int State
    {
        get { lock (_mu) { return _state; } }
    }

    public string LastError
    {
        get { lock (_mu) { return _lastError; } }
    }

    public long HeartbeatIntervalMs
    {
        get { lock (_mu) { return _heartbeatIntervalMs; } }
    }

    public void PushLog(string line)
    {
        lock (_mu)
        {
            if (_logs.Count >= Consts.LogQueueCap)
            {
                _logs.Dequeue(); // drop oldest, same as the C++ ring
            }
            _logs.Enqueue(line);
        }
    }

    public string? NextLog()
    {
        lock (_mu)
        {
            return _logs.Count > 0 ? _logs.Dequeue() : null;
        }
    }

    private void StopCore()
    {
        _stop.Set();
        if (_worker.IsAlive)
        {
            _worker.Join(TimeSpan.FromSeconds(2));
        }
        lock (_mu)
        {
            _state = Consts.StateDown;
        }
    }

    /// <summary>
    /// Placeholder worker: periodically allocates and touches state so the
    /// winecheck gate exercises managed threads, timers and the GC inside
    /// MT4/MT5 under Wine. The FIX session loop replaces this body later.
    /// </summary>
    private void WorkerLoop()
    {
        int tick = 0;
        while (!_stop.Wait(TimeSpan.FromMilliseconds(Consts.DeadlineSweepMs)))
        {
            tick++;
            if (tick % 20 == 0)
            {
                // Modest allocation churn to surface AOT/Wine GC issues early.
                PushLog($"bridge alive: tick={tick} host={Config.Host}:{Config.Port}");
            }
        }
    }
}

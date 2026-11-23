using QuickFix;

namespace FleetBridge;

/// <summary>
/// Shared bridge state: everything both the EA-facing FFI surface and the
/// QuickFIX session callbacks touch. All locks are short-lived; the FFI side
/// never blocks on the network and the FIX side never blocks on the EA.
/// Port of bridge/src/shared_state.cpp.
/// </summary>
internal sealed class SharedState
{
    /// <summary>
    /// Sends one message on the live FIX session; returns false when it
    /// could not be handed to the engine (session gone). Injected on logon,
    /// cleared on logout, so SharedState is testable without a live session.
    /// </summary>
    public delegate bool Sender(Message msg);

    private bool _shutdown;
    private int _state = Consts.StateDown;
    private long _heartbeatMs = Consts.DefaultHeartbeatMs;

    private readonly object _lastErrorMu = new();
    private string _lastError = "";

    private readonly object _queueMu = new();
    private readonly Queue<IncomingCmd> _queue = new();
    private readonly object _currentMu = new();
    private IncomingCmd? _current;

    /// <summary>
    /// command_id -> deadline for commands handed to the EA and not yet
    /// answered. Guarantees exactly one CommandResult per command_id.
    /// </summary>
    private readonly object _outstandingMu = new();
    private readonly Dictionary<string, long> _outstanding = new();

    private readonly object _seenMu = new();
    private readonly HashSet<string> _seenClientIds = new();
    private readonly Queue<string> _seenOrder = new();

    private readonly object _builderMu = new();
    private ResultBuilder? _builder;
    private readonly object _hbMu = new();
    private Heartbeat? _hbBuilder;
    private readonly object _depthMu = new();
    private MarketDepth? _depthBuilder;
    private readonly object _lastHbMu = new();
    private Heartbeat? _lastHeartbeat;

    /// <summary>Sender of the *current* engine session; null while disconnected.</summary>
    private readonly object _senderMu = new();
    private Sender? _sender;
    /// <summary>Sends that failed this session; surfaced in the bridge log.</summary>
    private long _dropped;

    /// <summary>
    /// Human-readable event log drained by the EA (Fleet_NextLog) into the
    /// terminal's Experts tab. Bounded; oldest lines drop first.
    /// </summary>
    private readonly object _logsMu = new();
    private readonly Queue<string> _logs = new();

    public bool Shutdown
    {
        get => Volatile.Read(ref _shutdown);
        set => Volatile.Write(ref _shutdown, value);
    }

    // ------------------------------------------------------------ event log

    public void LogLine(string line)
    {
        lock (_logsMu)
        {
            if (_logs.Count >= Consts.LogQueueCap)
            {
                _logs.Dequeue();
            }
            _logs.Enqueue(line);
        }
    }

    /// <summary>Pops the oldest log line; null when the log is drained.</summary>
    public string? NextLog()
    {
        lock (_logsMu)
        {
            return _logs.Count > 0 ? _logs.Dequeue() : null;
        }
    }

    // ----------------------------------------------------------- link state

    public int State => Volatile.Read(ref _state);
    public void SetState(int s) => Volatile.Write(ref _state, s);

    public long HeartbeatIntervalMs => Volatile.Read(ref _heartbeatMs);

    public void RecordError(string msg)
    {
        lock (_lastErrorMu)
        {
            _lastError = msg;
        }
    }

    public string LastError
    {
        get { lock (_lastErrorMu) { return _lastError; } }
    }

    /// <summary>
    /// Applies the engine's Welcome (Logon ack). Non-positive intervals keep
    /// the default; max tick batch size is ignored (ticks ship one per batch).
    /// </summary>
    public void ApplyWelcome(long heartbeatIntervalMs)
    {
        if (heartbeatIntervalMs > 0)
        {
            Volatile.Write(ref _heartbeatMs, heartbeatIntervalMs);
        }
    }

    public void SetSender(Sender? tx)
    {
        lock (_senderMu)
        {
            _sender = tx;
        }
    }

    /// <summary>
    /// Drops all per-session state after a disconnect. Session contract:
    /// a reconnect is a brand-new session and the engine re-issues work.
    /// </summary>
    public void ClearSession()
    {
        lock (_senderMu) { _sender = null; }
        lock (_queueMu) { _queue.Clear(); }
        lock (_outstandingMu) { _outstanding.Clear(); }
        lock (_currentMu) { _current = null; }
        lock (_builderMu) { _builder = null; }
        lock (_hbMu) { _hbBuilder = null; }
        lock (_depthMu) { _depthBuilder = null; }
        Interlocked.Exchange(ref _dropped, 0);
    }

    // ------------------------------------------------------------- outbound

    /// <summary>
    /// Best-effort send on the live session; drops when the engine link is
    /// down (ticks are expendable, results for a dead session are
    /// meaningless). Failed sends are counted and surfaced in the log.
    /// </summary>
    public void Send(Message msg)
    {
        Sender? tx;
        lock (_senderMu)
        {
            tx = _sender;
        }
        if (tx is null)
        {
            return; // link down: results for a dead session are meaningless
        }
        bool sent;
        try
        {
            sent = tx(msg);
        }
        catch
        {
            sent = false;
        }
        if (!sent)
        {
            long n = Interlocked.Increment(ref _dropped);
            if (n == 1 || n % 1000 == 0)
            {
                LogLine($"send failed: {n} events dropped this session (engine link unstable?)");
            }
        }
    }

    public void PushTick(Tick tick, AccountSnapshot account)
    {
        // Zero-latency tick path: every tick ships the moment the EA pushes
        // it, as a TickBatch of one, with the account snapshot riding along.
        if (State != Consts.StateUp)
        {
            return;
        }
        Send(Outbound.TickBatch(tick, account));
    }

    public void SendHeartbeat(Heartbeat hb)
    {
        lock (_lastHbMu)
        {
            _lastHeartbeat = hb;
        }
        Send(Outbound.TerminalStatus(hb));
    }

    public int BeginHeartbeat(bool terminalConnected, double equity, double balance, int pendingOrders)
    {
        var hb = new Heartbeat
        {
            TerminalConnected = terminalConnected,
            Equity = equity,
            Balance = balance,
            PendingOrders = pendingOrders,
        };
        lock (_hbMu)
        {
            _hbBuilder = hb;
        }
        return 0;
    }

    /// <summary>
    /// Routes a position row to whichever snapshot is being built: an open
    /// heartbeat takes precedence, otherwise the multi-row result builder.
    /// </summary>
    public int AddPosition(Position p)
    {
        lock (_hbMu)
        {
            if (_hbBuilder is { } hb)
            {
                hb.Positions.Add(p);
                return hb.Positions.Count;
            }
        }
        return WithBuilder(b =>
        {
            b.Positions.Add(p);
            return b.Positions.Count;
        });
    }

    public int EndHeartbeat()
    {
        Heartbeat? hb;
        lock (_hbMu)
        {
            hb = _hbBuilder;
            _hbBuilder = null;
        }
        if (hb is null)
        {
            return -1;
        }
        hb.UnixMs = Consts.NowMs();
        SendHeartbeat(hb);
        return 0;
    }

    public int BeginDepth(string symbol, long timeMs)
    {
        var d = new MarketDepth { Symbol = symbol, TimeUnixMs = timeMs };
        lock (_depthMu)
        {
            _depthBuilder = d;
        }
        return 0;
    }

    public int AddDepthRow(int side, double price, double volume)
    {
        lock (_depthMu)
        {
            if (_depthBuilder is null)
            {
                return -1;
            }
            _depthBuilder.Entries.Add(new BookEntry { Side = side, Price = price, Volume = volume });
            return _depthBuilder.Entries.Count;
        }
    }

    public int EndDepth()
    {
        // Zero-latency depth path, mirroring PushTick: shipped the moment
        // the EA seals it; while the link is down it is dropped, never
        // buffered.
        MarketDepth? d;
        lock (_depthMu)
        {
            d = _depthBuilder;
            _depthBuilder = null;
        }
        if (d is null)
        {
            return -1;
        }
        if (d.TimeUnixMs == 0)
        {
            d.TimeUnixMs = Consts.NowMs();
        }
        if (State != Consts.StateUp)
        {
            return 0;
        }
        Send(Outbound.DepthSnapshot(d));
        return 0;
    }

    /// <summary>
    /// Heartbeat sent in response to an engine ping: latest EA snapshot
    /// restamped, or a minimal one if the EA has not heartbeated yet.
    /// </summary>
    public Heartbeat PingHeartbeat()
    {
        Heartbeat hb;
        lock (_lastHbMu)
        {
            hb = _lastHeartbeat?.Clone() ?? new Heartbeat();
        }
        hb.UnixMs = Consts.NowMs();
        return hb;
    }

    // ------------------------------------------------------- command intake

    public void PushCommand(IncomingCmd cmd)
    {
        lock (_queueMu)
        {
            _queue.Enqueue(cmd);
        }
    }

    /// <summary>
    /// Emits a CommandResult error unless the id is empty (fire-and-forget
    /// commands must never produce a U6).
    /// </summary>
    private void AnswerError(string id, int code, string message, bool retryable)
    {
        if (id.Length == 0)
        {
            return;
        }
        Send(Outbound.ErrorResult(id, code, 0, message, retryable));
    }

    /// <summary>
    /// EA poll. Skips (and auto-answers) expired and duplicate commands so
    /// the EA only ever sees actionable work. Returns (type, command_id) or
    /// (0, "").
    /// </summary>
    public (int Type, string Id) Poll()
    {
        for (;;)
        {
            IncomingCmd? cmd = null;
            lock (_queueMu)
            {
                if (_queue.Count > 0)
                {
                    cmd = _queue.Dequeue();
                }
            }
            if (cmd is null)
            {
                return (0, "");
            }
            if (cmd.DeadlineMs > 0 && Consts.NowMs() > cmd.DeadlineMs)
            {
                LogLine($"command {cmd.Id} expired before execution; answered TIMEOUT");
                AnswerError(cmd.Id, Consts.ErrTimeout, "command deadline exceeded", true);
                continue;
            }
            if (cmd.ClientOrderId.Length != 0 && !RememberClientOrderId(cmd.ClientOrderId))
            {
                LogLine($"duplicate client_order_id '{cmd.ClientOrderId}'; answered DUPLICATE (command {cmd.Id})");
                AnswerError(cmd.Id, Consts.ErrDuplicate, "duplicate client_order_id", false);
                continue;
            }
            if (cmd.Id.Length != 0)
            {
                lock (_outstandingMu)
                {
                    _outstanding[cmd.Id] = cmd.DeadlineMs;
                }
            }
            lock (_currentMu)
            {
                _current = cmd;
            }
            return (cmd.CmdType, cmd.Id);
        }
    }

    public long CmdI64(string name)
    {
        lock (_currentMu)
        {
            return _current is not null && _current.Fields.TryGetValue(name, out Val v)
                ? v.AsI64() : 0;
        }
    }

    public double CmdF64(string name)
    {
        lock (_currentMu)
        {
            return _current is not null && _current.Fields.TryGetValue(name, out Val v)
                ? v.AsF64() : 0.0;
        }
    }

    public string CmdString(string name)
    {
        lock (_currentMu)
        {
            return _current is not null && _current.Fields.TryGetValue(name, out Val v)
                ? v.AsString() : "";
        }
    }

    private bool RememberClientOrderId(string id)
    {
        lock (_seenMu)
        {
            if (!_seenClientIds.Add(id))
            {
                return false;
            }
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > Consts.ClientOrderIdWindow)
            {
                _seenClientIds.Remove(_seenOrder.Dequeue());
            }
            return true;
        }
    }

    /// <summary>
    /// Answers expired commands (queued or already handed to the EA) with
    /// ERR_TIMEOUT. Late EA answers are dropped by Complete().
    /// </summary>
    public void SweepDeadlines()
    {
        long now = Consts.NowMs();

        var expiredQueued = new List<string>();
        lock (_queueMu)
        {
            int n = _queue.Count;
            for (int i = 0; i < n; i++)
            {
                IncomingCmd c = _queue.Dequeue();
                if (c.DeadlineMs > 0 && now > c.DeadlineMs)
                {
                    expiredQueued.Add(c.Id);
                }
                else
                {
                    _queue.Enqueue(c);
                }
            }
        }
        foreach (string id in expiredQueued)
        {
            LogLine($"command {id} expired in queue; answered TIMEOUT");
            AnswerError(id, Consts.ErrTimeout, "command deadline exceeded", true);
        }

        var expiredOutstanding = new List<string>();
        lock (_outstandingMu)
        {
            foreach (KeyValuePair<string, long> kv in _outstanding)
            {
                if (kv.Value > 0 && now > kv.Value)
                {
                    expiredOutstanding.Add(kv.Key);
                }
            }
            foreach (string id in expiredOutstanding)
            {
                _outstanding.Remove(id);
            }
        }
        foreach (string id in expiredOutstanding)
        {
            LogLine($"command {id} deadline exceeded while executing; answered TIMEOUT");
            AnswerError(id, Consts.ErrTimeout, "command deadline exceeded", true);
        }
    }

    // ------------------------------------------------------ command results

    /// <summary>
    /// Forwards a result iff the command is still outstanding; enforces the
    /// exactly-one-result-per-command_id contract. Returns -1 for unknown,
    /// already-answered or fire-and-forget (empty) ids.
    /// </summary>
    public int Complete(string id, Message result)
    {
        if (id.Length == 0)
        {
            return -1;
        }
        lock (_outstandingMu)
        {
            if (!_outstanding.Remove(id))
            {
                return -1; // unknown, already answered or stale session
            }
        }
        Send(result);
        return 0;
    }

    public int BeginResult(string id)
    {
        lock (_builderMu)
        {
            _builder = new ResultBuilder { Id = id };
        }
        return 0;
    }

    public int WithBuilder(Func<ResultBuilder, int> f)
    {
        lock (_builderMu)
        {
            return _builder is null ? -1 : f(_builder);
        }
    }

    public int EndResult(int payloadType)
    {
        ResultBuilder? b;
        lock (_builderMu)
        {
            b = _builder;
            _builder = null;
        }
        if (b is null)
        {
            return -1;
        }
        Message? msg = Outbound.RowsResult(b, payloadType);
        if (msg is null)
        {
            return -1;
        }
        return Complete(b.Id, msg);
    }

    /// <summary>Test hook: mirrors the C++/Rust tests poking `outstanding` directly.</summary>
    public void ForceOutstandingDeadline(string id, long deadlineMs)
    {
        lock (_outstandingMu)
        {
            _outstanding[id] = deadlineMs;
        }
    }
}

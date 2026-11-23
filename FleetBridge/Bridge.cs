using System.Reflection;
using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;

namespace FleetBridge;

/// <summary>
/// Owns one FIX initiator lifetime: dictionary materialization, QuickFIX
/// session settings, the network thread and the deadline sweeper. Port of
/// the Bridge struct in bridge/src/exports.cpp.
/// </summary>
internal sealed class Bridge
{
    public SharedState Shared { get; } = new();

    private FleetApp? _app;
    private SocketInitiator? _initiator;
    private Thread? _sweeper;
    private readonly ManualResetEventSlim _stopSweep = new(false);
    private string _dictPath = "";

    /// <summary>
    /// Throws on failure; the caller turns that into Fleet_LastError text.
    /// </summary>
    public void Start(BridgeConfig cfg)
    {
        // The embedded dictionary must exist on disk for QuickFIX to load.
        _dictPath = Path.Combine(
            Path.GetTempPath(), $"FIX44-FLEET-{Environment.ProcessId}.xml");
        using (Stream? src = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("FIX44-FLEET.xml"))
        {
            if (src is null)
            {
                throw new InvalidOperationException(
                    "embedded FIX dictionary FIX44-FLEET.xml not found");
            }
            using FileStream dst = File.Create(_dictPath);
            src.CopyTo(dst);
        }

        string settings =
            "[DEFAULT]\n" +
            "ConnectionType=initiator\n" +
            "ReconnectInterval=5\n" +
            "HeartBtInt=30\n" +
            "LogonTimeout=10\n" +
            "ResetOnLogon=Y\n" +
            "UseDataDictionary=Y\n" +
            $"DataDictionary={_dictPath}\n" +
            "ValidateUserDefinedFields=N\n" +
            "CheckLatency=N\n" +
            "StartTime=00:00:00\n" +
            "EndTime=00:00:00\n" +
            "[SESSION]\n" +
            "BeginString=FIX.4.4\n" +
            $"SenderCompID={cfg.TerminalId}\n" +
            "TargetCompID=STRATEGIST\n" +
            $"SocketConnectHost={cfg.Host}\n" +
            $"SocketConnectPort={cfg.Port}\n";

        _app = new FleetApp(cfg, Shared);
        _initiator = new SocketInitiator(
            _app,
            new MemoryStoreFactory(), // stateless: ResetOnLogon, no persistence
            new SessionSettings(new StringReader(settings)),
            new NullLogFactory());

        Shared.SetState(Consts.StateConnecting);
        _initiator.Start(); // spawns the QuickFIX network thread

        _sweeper = new Thread(SweepLoop)
        {
            IsBackground = true,
            Name = "fleet-deadline-sweeper",
        };
        _sweeper.Start();
    }

    private void SweepLoop()
    {
        while (!_stopSweep.Wait(TimeSpan.FromMilliseconds(Consts.DeadlineSweepMs)))
        {
            if (Shared.Shutdown)
            {
                break;
            }
            Shared.SweepDeadlines();
        }
    }

    /// <summary>Bounded: never leave the terminal hanging on chart close.</summary>
    public void Stop()
    {
        Shared.Shutdown = true;
        _stopSweep.Set();
        try
        {
            _initiator?.Stop(true); // force: don't wait for logout
        }
        catch
        {
            // best effort
        }
        if (_sweeper is { IsAlive: true })
        {
            _sweeper.Join(TimeSpan.FromSeconds(2));
        }
        Shared.SetState(Consts.StateDown);
        if (_dictPath.Length != 0)
        {
            try
            {
                File.Delete(_dictPath);
            }
            catch
            {
                // best effort
            }
        }
    }
}

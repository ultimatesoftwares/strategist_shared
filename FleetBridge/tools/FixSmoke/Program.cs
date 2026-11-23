// QuickFIX/n-under-NativeAOT gate (see FixSmoke.csproj).
// Boots an acceptor and an initiator in-process on loopback with the
// FIX44-FLEET dictionary and asserts both sides log on within 15 seconds.

using System.Net;
using System.Net.Sockets;
using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;

string dictionary = Path.Combine(AppContext.BaseDirectory, "spec", "FIX44-FLEET.xml");
if (!File.Exists(dictionary))
{
    Console.Error.WriteLine($"FAIL: dictionary not found: {dictionary}");
    return 2;
}

// Grab a free loopback port.
int port;
using (var probe = new TcpListener(IPAddress.Loopback, 0))
{
    probe.Start();
    port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
}

string acceptorCfg = $"""
    [DEFAULT]
    ConnectionType=acceptor
    StartTime=00:00:00
    EndTime=00:00:00
    HeartBtInt=5
    UseDataDictionary=Y
    DataDictionary={dictionary}
    SocketAcceptHost=127.0.0.1
    SocketAcceptPort={port}

    [SESSION]
    BeginString=FIX.4.4
    SenderCompID=ENGINE
    TargetCompID=TERMINAL
    """;

string initiatorCfg = $"""
    [DEFAULT]
    ConnectionType=initiator
    ReconnectInterval=1
    StartTime=00:00:00
    EndTime=00:00:00
    HeartBtInt=5
    UseDataDictionary=Y
    DataDictionary={dictionary}
    SocketConnectHost=127.0.0.1
    SocketConnectPort={port}

    [SESSION]
    BeginString=FIX.4.4
    SenderCompID=TERMINAL
    TargetCompID=ENGINE
    """;

var acceptorApp = new SmokeApp("acceptor");
var initiatorApp = new SmokeApp("initiator");

var acceptor = new ThreadedSocketAcceptor(
    acceptorApp,
    new MemoryStoreFactory(),
    new SessionSettings(new StringReader(acceptorCfg)),
    new NullLogFactory());
var initiator = new SocketInitiator(
    initiatorApp,
    new MemoryStoreFactory(),
    new SessionSettings(new StringReader(initiatorCfg)),
    new NullLogFactory());

try
{
    acceptor.Start();
    initiator.Start();

    var deadline = TimeSpan.FromSeconds(15);
    bool acceptorUp = acceptorApp.LoggedOn.Wait(deadline);
    bool initiatorUp = initiatorApp.LoggedOn.Wait(deadline);

    if (!acceptorUp || !initiatorUp)
    {
        Console.Error.WriteLine(
            $"FAIL: logon timed out (acceptor={acceptorUp}, initiator={initiatorUp})");
        return 1;
    }

    Console.WriteLine("FIX SMOKE PASSED: both sides logged on with FIX44-FLEET.xml");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAIL: {e}");
    return 1;
}
finally
{
    try { initiator.Stop(true); } catch { /* best effort */ }
    try { acceptor.Stop(true); } catch { /* best effort */ }
}

sealed class SmokeApp(string side) : IApplication
{
    public ManualResetEventSlim LoggedOn { get; } = new(false);

    public void OnCreate(SessionID sessionId) { }

    public void OnLogon(SessionID sessionId)
    {
        Console.WriteLine($"{side}: logon {sessionId}");
        LoggedOn.Set();
    }

    public void OnLogout(SessionID sessionId) { }
    public void ToAdmin(Message message, SessionID sessionId) { }
    public void FromAdmin(Message message, SessionID sessionId) { }
    public void ToApp(Message message, SessionID sessionId) { }
    public void FromApp(Message message, SessionID sessionId) { }
}

// QuickFIX/n-under-NativeAOT gate (see FixSmoke.csproj).
// Boots an acceptor and an initiator in-process on loopback with the
// FIX44-FLEET dictionary, asserts both sides log on within 15 seconds, then
// round-trips a grouped application message (U7 SubscribeSymbols). The group
// leg is load-bearing: with QuickFIX/n's DefaultMessageFactory and no
// generated FIX.4.4 assembly, flat messages (logon) parse fine but any
// repeating group throws and kills the session — exactly the wiring bug
// FleetMessageFactory exists to prevent. Logon-only smoke would miss it.

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

    ValidateUserDefinedFields=N

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

    ValidateUserDefinedFields=N

    [SESSION]
    BeginString=FIX.4.4
    SenderCompID=TERMINAL
    TargetCompID=ENGINE
    """;

var acceptorApp = new SmokeApp("acceptor");
var initiatorApp = new SmokeApp("initiator");

// Same factory wiring as production (bot acceptor / bridge initiator):
// groups must come from the data dictionary, never from generated types.
var acceptor = new ThreadedSocketAcceptor(
    acceptorApp,
    new MemoryStoreFactory(),
    new SessionSettings(new StringReader(acceptorCfg)),
    new NullLogFactory(),
    new FleetBridge.FleetMessageFactory());
var initiator = new SocketInitiator(
    initiatorApp,
    new MemoryStoreFactory(),
    new SessionSettings(new StringReader(initiatorCfg)),
    new NullLogFactory(),
    new FleetBridge.FleetMessageFactory());

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

    // Engine -> terminal grouped command, as the bot sends right after logon.
    var u7 = new Message();
    u7.Header.SetField(new QuickFix.Fields.MsgType("U7"));
    foreach (string symbol in new[] { "BITCOIN_i", "ETHEREUM_i" })
    {
        var entry = new Group(20400, 20401);
        entry.SetField(new QuickFix.Fields.StringField(20401, symbol));
        u7.AddGroup(entry);
    }
    if (!Session.SendToTarget(u7, new SessionID("FIX.4.4", "ENGINE", "TERMINAL")))
    {
        Console.Error.WriteLine("FAIL: could not send U7 SubscribeSymbols");
        return 1;
    }

    if (!initiatorApp.GroupedAppMessage.Wait(deadline))
    {
        Console.Error.WriteLine("FAIL: grouped U7 not received (group parsing broken?)");
        return 1;
    }
    if (initiatorApp.GroupedSymbols != "BITCOIN_i,ETHEREUM_i")
    {
        Console.Error.WriteLine(
            $"FAIL: U7 group decoded as '{initiatorApp.GroupedSymbols}', expected 'BITCOIN_i,ETHEREUM_i'");
        return 1;
    }

    Console.WriteLine(
        "FIX SMOKE PASSED: both sides logged on and a grouped U7 round-tripped with FIX44-FLEET.xml");
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
    public ManualResetEventSlim GroupedAppMessage { get; } = new(false);
    public string GroupedSymbols { get; private set; } = "";

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

    public void FromApp(Message message, SessionID sessionId)
    {
        if (message.Header.GetString(QuickFix.Fields.Tags.MsgType) != "U7")
        {
            return;
        }
        int n = message.GroupCount(20400);
        var symbols = new List<string>(n);
        for (int i = 1; i <= n; i++)
        {
            symbols.Add(message.GetGroup(i, 20400).GetString(20401));
        }
        GroupedSymbols = string.Join(',', symbols);
        Console.WriteLine($"{side}: U7 received with symbols [{GroupedSymbols}]");
        GroupedAppMessage.Set();
    }
}

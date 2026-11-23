// Port of the C++ bridge's tests/state_tests.cpp (itself a port of
// crates/bridge/src/state/tests.rs): SharedState command-queue semantics and
// assertions on the FIX messages produced through a capturing sender.
// Exit 0 = all checks passed.

using FleetBridge;
using QuickFix;
using QuickFix.Fields;

int failures = 0;
int checks = 0;

void Check(bool cond, string what)
{
    checks++;
    if (!cond)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {what}");
    }
}

string MsgTypeOf(Message m) => m.Header.GetString(Tags.MsgType);

IncomingCmd Cmd(string id, string coid, long deadlineMs) => new()
{
    Id = id,
    CmdType = Consts.CmdPlaceOrder,
    DeadlineMs = deadlineMs,
    ClientOrderId = coid,
};

// ---- poll returns queued commands in order --------------------------------
{
    var s = new SharedState();
    s.PushCommand(Cmd("a", "", 0));
    s.PushCommand(Cmd("b", "", 0));
    Check(s.Poll() == (Consts.CmdPlaceOrder, "a"), "poll order: first is a");
    Check(s.Poll() == (Consts.CmdPlaceOrder, "b"), "poll order: second is b");
    Check(s.Poll() == (0, ""), "poll order: drained");
}

// ---- duplicate client_order_ids never reach the EA -------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(Cmd("a", "oid-1", 0));
    s.PushCommand(Cmd("b", "oid-1", 0));
    Check(s.Poll().Id == "a", "duplicate: first poll is a");
    Check(s.Poll().Type == 0, "duplicate: replay auto-answered");
    Check(cap.Count == 1, "duplicate: one U6 sent");
    if (cap.Count == 1)
    {
        Message m = cap[0];
        Check(MsgTypeOf(m) == Msg.CommandResult, "duplicate: U6 msg type");
        Check(m.GetString(Tag.FltCommandId) == "b", "duplicate: command id b");
        Check(m.GetString(Tag.FltOk) == "N", "duplicate: ok=N");
        Check(m.GetString(Tag.FltErrorCode) == Consts.ErrDuplicate.ToString(), "duplicate: error code");
        Check(m.GetString(Tag.FltErrorRetryable) == "N", "duplicate: not retryable");
    }
}

// ---- expired commands never reach the EA -----------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(Cmd("a", "", Consts.NowMs() - 1));
    Check(s.Poll().Type == 0, "expired: auto-answered with TIMEOUT");
    Check(cap.Count == 1, "expired: one U6 sent");
    if (cap.Count == 1)
    {
        Message m = cap[0];
        Check(m.GetString(Tag.FltCommandId) == "a", "expired: command id");
        Check(m.GetString(Tag.FltErrorCode) == Consts.ErrTimeout.ToString(), "expired: error code");
        Check(m.GetString(Tag.FltErrorRetryable) == "Y", "expired: retryable");
    }
}

// ---- fire-and-forget commands are never answered ---------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    // Expired empty-id command: dropped silently (U6 requires a command id).
    s.PushCommand(Cmd("", "", Consts.NowMs() - 1));
    Check(s.Poll().Type == 0, "fire-and-forget: expired dropped");
    Check(cap.Count == 0, "fire-and-forget: no U6 for empty id");
    // Live empty-id command: executed but an ack attempt is rejected.
    s.PushCommand(Cmd("", "", 0));
    Check(s.Poll().Type == Consts.CmdPlaceOrder, "fire-and-forget: live polled");
    Check(s.Complete("", Outbound.AckResult("")) == -1, "fire-and-forget: ack rejected");
    Check(cap.Count == 0, "fire-and-forget: still no U6");
}

// ---- complete is exactly once per command id --------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(Cmd("a", "", 0));
    (_, string id) = s.Poll();
    Check(s.Complete(id, Outbound.AckResult(id)) == 0, "once: first complete ok");
    Check(s.Complete(id, Outbound.AckResult(id)) == -1, "once: second complete rejected");
    Check(s.Complete("never-issued", Outbound.AckResult("never-issued")) == -1, "once: unknown rejected");
    Check(cap.Count == 1, "once: one U6 sent");
    if (cap.Count == 1)
    {
        Check(cap[0].GetString(Tag.FltOk) == "Y", "once: ok=Y");
        Check(cap[0].GetString(Tag.FltPayloadType) == "0", "once: payload none");
    }
}

// ---- swept commands drop late EA answers -----------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(Cmd("a", "", Consts.NowMs() + 10_000));
    (_, string id) = s.Poll();
    // Simulate the deadline passing while the EA works on it.
    s.ForceOutstandingDeadline(id, Consts.NowMs() - 1);
    s.SweepDeadlines();
    Check(s.Complete(id, Outbound.AckResult(id)) == -1, "sweep: late answer dropped");
    Check(cap.Count == 1, "sweep: only the TIMEOUT went out");
    if (cap.Count == 1)
    {
        Check(cap[0].GetString(Tag.FltErrorCode) == Consts.ErrTimeout.ToString(), "sweep: error code");
    }
}

// ---- current command fields are typed loosely --------------------------------
{
    var s = new SharedState();
    var c = new IncomingCmd { Id = "a", CmdType = Consts.CmdPlaceOrder };
    c.Fields["symbol"] = Val.Str("XAUUSD");
    c.Fields["volume"] = Val.F64(0.5);
    c.Fields["magic_number"] = Val.I64(42);
    s.PushCommand(c);
    s.Poll();
    Check(s.CmdString("symbol") == "XAUUSD", "fields: string");
    Check(s.CmdF64("volume") == 0.5, "fields: double");
    Check(s.CmdI64("magic_number") == 42, "fields: int");
    Check(s.CmdI64("volume") == 0, "fields: truncation semantics");
    Check(s.CmdString("missing").Length == 0, "fields: missing is empty");
}

// ---- result builder assembles payloads ---------------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(new IncomingCmd { Id = "h1", CmdType = Consts.CmdGetHistory });
    s.Poll();
    Check(s.BeginResult("h1") == 0, "builder: begin");
    Check(s.WithBuilder(b => { b.Bars.Add(new Bar()); return b.Bars.Count; }) == 1, "builder: add bar");
    Check(s.EndResult(Consts.PayloadHistory) == 0, "builder: end");
    Check(s.EndResult(Consts.PayloadHistory) == -1, "builder: consumed");
    Check(cap.Count == 1, "builder: one U6 sent");
    if (cap.Count == 1)
    {
        Message m = cap[0];
        Check(MsgTypeOf(m) == Msg.CommandResult, "builder: msg type");
        Check(m.GetString(Tag.FltCommandId) == "h1", "builder: command id");
        Check(m.GetString(Tag.FltOk) == "Y", "builder: ok=Y");
        Check(m.GetString(Tag.FltPayloadType) == Consts.WirePayloadHistory.ToString(), "builder: payload type");
        Check(m.GetString(Tag.NoFltBars) == "1", "builder: NoFltBars=1");
    }
}

// ---- unknown payload type fails end_result ------------------------------------
{
    var s = new SharedState();
    Check(s.BeginResult("x") == 0, "unknown payload: begin");
    Check(s.EndResult(99) == -1, "unknown payload: end fails");
}

// ---- heartbeat builder collects positions and feeds pings ----------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    Check(s.EndHeartbeat() == -1, "hb: nothing begun");
    Check(s.AddPosition(new Position()) == -1, "hb: no builder active");
    Check(s.BeginHeartbeat(true, 100.0, 90.0, 3) == 0, "hb: begin");
    Check(s.AddPosition(new Position()) == 1, "hb: first position");
    Check(s.AddPosition(new Position()) == 2, "hb: second position");
    Check(s.EndHeartbeat() == 0, "hb: end");
    Check(s.EndHeartbeat() == -1, "hb: builder consumed");
    Check(cap.Count == 1, "hb: one U2 sent");
    if (cap.Count == 1)
    {
        Message m = cap[0];
        Check(MsgTypeOf(m) == Msg.TerminalStatus, "hb: msg type");
        Check(m.GetString(Tag.FltTerminalConnected) == "Y", "hb: connected");
        Check(m.GetString(Tag.FltPendingOrderCount) == "3", "hb: pending orders");
        Check(m.GetString(Tag.NoFltPositions) == "2", "hb: NoFltPositions=2");
    }
    // Ping replays the latest snapshot, restamped.
    Heartbeat hb = s.PingHeartbeat();
    Check(hb.TerminalConnected, "hb: ping connected");
    Check(hb.Positions.Count == 2, "hb: ping positions");
    Check(hb.PendingOrders == 3, "hb: ping pending orders");
    Check(hb.UnixMs > 0, "hb: ping restamped");
}

// ---- depth builder assembles and consumes snapshots ----------------------------
{
    var s = new SharedState();
    Check(s.EndDepth() == -1, "depth: nothing begun");
    Check(s.AddDepthRow(1, 4128.65, 5.0) == -1, "depth: no builder active");
    Check(s.BeginDepth("XAUUSD", 0) == 0, "depth: begin");
    Check(s.AddDepthRow(1, 4128.65, 5.0) == 1, "depth: ask row");
    Check(s.AddDepthRow(2, 4128.25, 3.0) == 2, "depth: bid row");
    // Link is DOWN in tests, so the snapshot is dropped, not an error.
    Check(s.EndDepth() == 0, "depth: end drops while down");
    Check(s.EndDepth() == -1, "depth: builder consumed");
}

// ---- ticks ship only while the session is up ------------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    var t = new Tick
    {
        Symbol = "EURUSD",
        Bid = 1.1,
        Ask = 1.2,
        Volume = 7,
        TimeUnixMs = 1234,
        SpreadPoints = 10,
    };
    var acc = new AccountSnapshot { Balance = 1000.0, Equity = 1001.0, Margin = 10.0, MarginFree = 990.0 };
    s.PushTick(t, acc); // state DOWN: dropped
    Check(cap.Count == 0, "tick: dropped while down");
    s.SetState(Consts.StateUp);
    s.PushTick(t, acc);
    Check(cap.Count == 1, "tick: shipped while up");
    if (cap.Count == 1)
    {
        Message m = cap[0];
        Check(MsgTypeOf(m) == Msg.TickBatch, "tick: msg type");
        Check(m.GetString(Tag.NoFltTicks) == "1", "tick: NoFltTicks=1");
        Check(m.GetString(Tag.FltAccBalance) == "1000", "tick: balance formatting");
        Group g = m.GetGroup(1, Tag.NoFltTicks);
        Check(g.GetString(Tag.FltTickSymbol) == "EURUSD", "tick: group symbol");
        Check(g.GetString(Tag.FltTickVolume) == "7", "tick: group volume");
        Check(g.GetString(Tag.FltTickTimeMs) == "1234", "tick: group time");
    }
}

// ---- welcome only applies positive heartbeat intervals ---------------------------
{
    var s = new SharedState();
    Check(s.HeartbeatIntervalMs == Consts.DefaultHeartbeatMs, "welcome: default");
    s.ApplyWelcome(0);
    Check(s.HeartbeatIntervalMs == Consts.DefaultHeartbeatMs, "welcome: zero ignored");
    s.ApplyWelcome(5000);
    Check(s.HeartbeatIntervalMs == 5000, "welcome: positive applied");
}

// ---- clear_session wipes per-session state ---------------------------------------
{
    var s = new SharedState();
    var cap = new List<Message>();
    s.SetSender(m => { cap.Add(m); return true; });
    s.PushCommand(Cmd("a", "", 0));
    s.Poll();
    s.ClearSession();
    // Outstanding map gone: a late answer is rejected and nothing is sent.
    Check(s.Complete("a", Outbound.AckResult("a")) == -1, "clear: late answer rejected");
    Check(cap.Count == 0, "clear: nothing sent");
}

// ---- utf16 helpers round-trip and truncate ---------------------------------------
unsafe
{
    const string original = "EURUSD \u00e9\u20ac"; // "EURUSD é€"
    ushort* buf = stackalloc ushort[64];
    int n = Wstr.WriteUtf16(original, buf, 64);
    Check(n > 0, "wstr: wrote units");
    Check(Wstr.FromUtf16(buf) == original, "wstr: round-trip");
    // Truncation: capacity 4 -> 3 units + NUL.
    ushort* small = stackalloc ushort[4];
    Check(Wstr.WriteUtf16("ABCDEF", small, 4) == 3, "wstr: truncates to 3");
    Check(Wstr.FromUtf16(small) == "ABC", "wstr: truncated content");
    // Null in, empty out.
    Check(Wstr.FromUtf16(null).Length == 0, "wstr: null reads empty");
    Check(Wstr.WriteUtf16("x", null, 8) == 0, "wstr: null write is 0");
}

if (failures > 0)
{
    Console.Error.WriteLine($"STATE TESTS FAILED: {failures}/{checks} checks failed");
    return 1;
}

Console.WriteLine($"STATE TESTS PASSED: {checks} checks");
return 0;

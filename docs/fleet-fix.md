# FIX44-FLEET — the fleet wire contract

FIX 4.4 session layer with user-defined application messages (tags 20000+).
One dialect, three consumers:

| Component | Role | Engine |
|---|---|---|
| strategist_bot | acceptor (`bridge.fixPort`, default **5090**) | QuickFIX/n |
| MT4/MT5 terminal bridge | initiator, terminal plane | QuickFIX C++ |
| desktop client | initiator, feed plane | any FIX engine |

The data dictionary
[`shared/FleetBridge/spec/FIX44-FLEET.xml`](../FleetBridge/spec/FIX44-FLEET.xml)
**is** the wire contract — message shapes, tag numbers, group layouts and
required/optional flags live there and only there. This document covers the
semantics the XML cannot express. On the bot side the only code allowed to
touch raw tags is `Fleet/Fix/FleetFixCodec.cs`; its `FleetTags` /
`FleetMsgTypes` constants must match the XML exactly.

## Session layer

Two session planes share the acceptor port, selected by the **TargetCompID**
the initiator logs on with:

| Plane | Initiator SenderCompID | Initiator TargetCompID |
|---|---|---|
| terminal (MT4/MT5 bridge) | terminal id (stable per terminal) | `STRATEGIST` |
| feed (desktop client) | client id | `STRATEGIST-FEED` |

Sessions are **stateless** by contract:

- `ResetOnLogon=Y`, `ResetOnDisconnect=Y`, no message persistence, no
  resend-based recovery. A disconnect loses nothing that the logon contract
  does not rebuild: the engine re-issues desired market-data subscriptions
  after every logon, and connectors keep no cross-session state.
- Liveness is the FIX session layer's job (`HeartBtInt`); the application
  never implements its own ping timer on the terminal plane (U23 FleetPing
  exists for the engine to request an immediate status, not for liveness).
- 64-bit integers (unix-ms timestamps, tickets, magic numbers, logins, tick
  volumes) are typed STRING in the dictionary to dodge 32-bit INT
  validation. All timestamps are unix **milliseconds** UTC.
- Optional fields equal to their default (0, empty string, false) are
  omitted on the wire and decoded back to the default. Repeating-group
  entries list fields in dictionary order; the first field is the group
  delimiter and is **always** present per entry, even when zero.

### Dynamic session provisioning

QuickFIX/n has no wildcard acceptor sessions, so peers are provisioned on
demand:

1. A brand-new CompID connects and sends Logon. The acceptor has no session
   for it: the Logon is observed, a concrete session for that CompID is
   registered, and the connection is dropped.
2. The initiator's standard reconnect (keep `ReconnectInterval` short; 1s in
   tests) logs on normally and stays up.

A second connection for an already-connected CompID is refused by the
session layer (one responder per session); on the terminal plane a duplicate
terminal id is additionally refused with a Logon reject
(`terminal '<id>' already has an active session`).

## Terminal plane

### Logon handshake

The **Hello rides the initiator's Logon** as custom tags (20010–20017):
platform (1=MT4, 2=MT5), terminal build, connector version, account login,
broker server, server TZ offset minutes, margin mode, broker name.

The **Welcome rides the engine's Logon reply** (20020–20022): engine session
id, max tick batch size, status heartbeat interval ms. The connector must
push U2 TerminalStatus every `FltHeartbeatIntervalMs` and cap U3 TickBatch
at `FltMaxTickBatchSize` ticks.

After logon the engine (in order) requests the symbol catalog, requests
account info, and re-issues the desired subscriptions (U7/U9). The
connector just answers.

### Messages

Events up (connector → engine):

| Type | Message | Semantics |
|---|---|---|
| U2 | TerminalStatus | Rolling health + **full** open-position snapshot, every `FltHeartbeatIntervalMs` and immediately on U23 FleetPing. |
| U3 | TickBatch | 1..N chronological ticks + account state sampled at send time. |
| U4 | MarketDepthSnapshot | The FULL book on every change, entries exactly as the terminal reports them (sells first, then buys, price descending). |
| U5 | TradeEvent | Trade-state change push. `FltTradeSubject` selects the flat field set present: 1=position (`FltPos*`), 2=pending order (`FltOrd*`), 0=neither; `FltDeal*` ride along when the platform reports a deal. |
| U6 | CommandResult | See command correlation below. |
| U24 | LogEvent | Noteworthy connector-side diagnostics (level 1..4). |

Commands down (engine → connector): U7/U8 (un)subscribe ticks, U9/U10
(un)subscribe depth, U11 PlaceOrder, U12 ClosePosition, U13
ClosePositionsBatch, U14 ModifyPosition, U15 ModifyOrder, U16 CancelOrder,
U17 GetAccountInfo, U18 GetPositions, U19 GetPendingOrders, U20
GetSymbolInfo, U21 GetHistory, U22 GetDealsHistory, U23 FleetPing.

### Command correlation

- Every command carries `FltCommandId` (20001). Empty/absent = fire and
  forget. An optional absolute `FltDeadlineMs` caps execution.
- The connector answers **exactly one** U6 CommandResult per non-empty
  `FltCommandId`, echoing it — even on failure, timeout, or unsupported
  command (`FltOk=N` + `FltError*`). `FltPayloadType` selects the result
  payload: 0 none, 1 order, 2 accountInfo, 3 history, 4 positions,
  5 pendingOrders, 6 symbolInfo, 7 deals, 8 batchClose.
- U11 PlaceOrder is **idempotent by `FltClientOrderId`**: if a result was
  lost to a disconnect and the engine retries the same client order id, the
  connector must not place a second order — it reports the existing one.
- U23 FleetPing requests an immediate U2 TerminalStatus; no CommandResult.

Enum values on the wire are the integer values of the enums in
`Fleet/FleetContracts.cs` (Platform, OrderType, Timeframe, ErrorCode,
MarginMode, DealEntry, BookSide where 1=sell/ask, 2=buy/bid, TradeEvent
kind, log level). `Unspecified=0` everywhere.

## Feed plane

Desktop clients log on (no custom Logon tags) and subscribe:

| Type | Message | Semantics |
|---|---|---|
| U30 | FeedSubscribe | `FltFeedPrefix` filters subjects by prefix (empty/absent = all). Replay of the latest event per matching subject, then live stream. At most one active per session; a new U30 replaces the previous subscription. |
| U31 | FeedEvent | One subject-addressed JSON payload (subject, JSON, engine receive time). The engine-state plane of `shared/Contracts/V1` rides here. |
| U32 | FeedDepthSubscribe | 1..128 `(FltVenueId, FltInstrumentId)` selectors; duplicates are collapsed. At most one active per session; a new U32 replaces the previous selector set. |
| U33 | FeedDepthUpdate | Full book per update, echoing the selector's venue/instrument verbatim. Entries: side (1=sell, 2=buy), price, volume. |

Feed subscribers are served through bounded drop-oldest buffers: a slow
client loses its own oldest updates, never the engine's.

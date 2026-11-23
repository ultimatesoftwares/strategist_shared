namespace FleetBridge;

/// <summary>
/// Protocol/ABI constants shared by the FFI surface, the FIX session and the
/// EAs. Values mirror the C++ bridge (bridge/src/consts.h); the EA-facing
/// ones must match the defines in the MQL EAs.
/// </summary>
internal static class Consts
{
    public const int AbiVersion = 10;

    // Connection states reported by Fleet_State().
    public const int StateDown = 0;
    public const int StateConnecting = 1;
    public const int StateUp = 2;

    // ErrorCode values the bridge emits on its own behalf.
    public const int ErrUnsupported = 2;
    public const int ErrTimeout = 13;
    public const int ErrDuplicate = 17;

    // Command type ids handed to the EA; must match the CMD_* defines in the EAs.
    public const int CmdSubscribe = 1;
    public const int CmdUnsubscribe = 2;
    public const int CmdPlaceOrder = 3;
    public const int CmdClosePosition = 4;
    public const int CmdGetAccountInfo = 5;
    public const int CmdGetHistory = 6;
    public const int CmdGetPositions = 7;
    public const int CmdGetPendingOrders = 8;
    public const int CmdModifyPosition = 9;
    public const int CmdCancelOrder = 10;
    public const int CmdModifyOrder = 11;
    public const int CmdGetSymbolInfo = 12;
    public const int CmdGetDealsHistory = 13;
    public const int CmdClosePositionsBatch = 14;
    public const int CmdSubscribeDepth = 15;
    public const int CmdUnsubscribeDepth = 16;

    // Payload type ids accepted by Fleet_EndResult; must match the EA defines.
    public const int PayloadHistory = 1;
    public const int PayloadPositions = 2;
    public const int PayloadPending = 3;
    public const int PayloadSymbols = 4;
    public const int PayloadDeals = 5;
    public const int PayloadBatchClose = 6;

    // FltPayloadType values on the wire (CommandResult U6). The EA-facing
    // PAYLOAD_* ids above map onto these when the FIX transport is wired up.
    public const int WirePayloadNone = 0;
    public const int WirePayloadOrder = 1;
    public const int WirePayloadAccountInfo = 2;
    public const int WirePayloadHistory = 3;
    public const int WirePayloadPositions = 4;
    public const int WirePayloadPending = 5;
    public const int WirePayloadSymbols = 6;
    public const int WirePayloadDeals = 7;
    public const int WirePayloadBatchClose = 8;

    public const long DefaultHeartbeatMs = 15_000;
    public const int ClientOrderIdWindow = 1024;
    public const int LogQueueCap = 512;
    public const long DeadlineSweepMs = 500;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

using System.Globalization;

namespace FleetBridge;

/// <summary>
/// Number -> string formatting for FIX field values. Doubles use .NET's
/// shortest round-trip formatting, matching the C++ bridge's std::to_chars
/// (and Rust's f64::to_string before it).
/// </summary>
internal static class Numfmt
{
    public static string I64(long v) => v.ToString(CultureInfo.InvariantCulture);
    public static string U64(ulong v) => v.ToString(CultureInfo.InvariantCulture);
    public static string F64(double v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Connection parameters captured at Fleet_Init time.</summary>
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
/// A flattened command field value, typed loosely the way MQL consumes it.
/// Field names mirror the old mtfleet.v1 protobuf messages; the EA reads
/// command fields back by these exact names via Fleet_CmdInt/Double/String.
/// </summary>
internal readonly struct Val
{
    private enum Kind { Str, Int, Flt }

    private readonly Kind _kind;
    private readonly string? _s;
    private readonly long _i;
    private readonly double _f;

    private Val(Kind kind, string? s, long i, double f)
    {
        _kind = kind;
        _s = s;
        _i = i;
        _f = f;
    }

    public static Val Str(string v) => new(Kind.Str, v, 0, 0.0);
    public static Val I64(long v) => new(Kind.Int, null, v, 0.0);
    public static Val F64(double v) => new(Kind.Flt, null, 0, v);

    public long AsI64() => _kind switch
    {
        Kind.Int => _i,
        Kind.Flt => (long)_f,
        _ => long.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0,
    };

    public double AsF64() => _kind switch
    {
        Kind.Int => _i,
        Kind.Flt => _f,
        _ => double.TryParse(_s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0,
    };

    public string AsString() => _kind switch
    {
        Kind.Str => _s ?? "",
        Kind.Int => Numfmt.I64(_i),
        _ => Numfmt.F64(_f),
    };
}

internal sealed class IncomingCmd
{
    public string Id = "";
    public int CmdType;
    public long DeadlineMs; // 0 = none
    public string ClientOrderId = ""; // empty = none
    public Dictionary<string, Val> Fields = new();
}

// ------------------------------------------------------------ market data

internal sealed class Tick
{
    public string Symbol = "";
    public double Bid, Ask, Last;
    public ulong Volume;
    public long TimeUnixMs;
    public int SpreadPoints;
}

internal sealed class AccountSnapshot
{
    public double Balance, Equity, Margin, MarginFree;
}

internal sealed class BookEntry
{
    public int Side; // 1 = sell/ask, 2 = buy/bid
    public double Price, Volume;
}

internal sealed class MarketDepth
{
    public string Symbol = "";
    public long TimeUnixMs;
    public List<BookEntry> Entries = new();
}

// ------------------------------------------------------------ trade state

internal sealed class Position
{
    public ulong Ticket;
    public string Symbol = "";
    public int Type;
    public double Volume, PriceOpen, PriceCurrent;
    public double StopLoss, TakeProfit;
    public double Profit, Swap, Commission;
    public string Comment = "";
    public long MagicNumber;
    public long OpenTimeUnixMs;
}

internal sealed class PendingOrder
{
    public ulong Ticket;
    public string Symbol = "";
    public int Type;
    public double VolumeInitial, VolumeCurrent;
    public double Price, PriceCurrent, StopLimitPrice;
    public double StopLoss, TakeProfit;
    public string Comment = "";
    public long MagicNumber;
    public long SetupTimeUnixMs;
    public long ExpirationUnixMs;
}

internal sealed class Deal
{
    public ulong Ticket, OrderTicket, PositionTicket;
    public string Symbol = "";
    public int Type;
    public double Volume, Price;
    public double Profit, Swap, Commission;
    public string Comment = "";
    public long MagicNumber;
    public long TimeUnixMs;
    public int Entry;
    public double? Fee;
}

internal sealed class Heartbeat
{
    public long UnixMs;
    public bool TerminalConnected;
    public double Equity, Balance;
    public int PendingOrders;
    public List<Position> Positions = new();

    public Heartbeat Clone() => new()
    {
        UnixMs = UnixMs,
        TerminalConnected = TerminalConnected,
        Equity = Equity,
        Balance = Balance,
        PendingOrders = PendingOrders,
        Positions = new List<Position>(Positions),
    };
}

// --------------------------------------------------------------- results

internal sealed class CmdError
{
    public int Code;
    public int PlatformCode;
    public string Message = "";
    public bool Retryable;
}

internal sealed class Bar
{
    public long TimeUnixMs;
    public double Open, High, Low, Close;
    public ulong TickVolume, RealVolume;
    public int Spread;
}

internal sealed class SymbolInfo
{
    public string Symbol = "", Description = "";
    public int Digits;
    public double Point, TickSize, TickValue, ContractSize;
    public double VolumeMin, VolumeMax, VolumeStep;
    public int StopsLevelPoints, FreezeLevelPoints;
    public double MarginRequired;
    public string CurrencyBase = "", CurrencyProfit = "", CurrencyMargin = "";
    public bool TradeAllowed;
    public long SpreadPoints;
}

internal sealed class CloseResult
{
    public ulong Ticket;
    public bool Ok;
    public double ClosedVolume, ClosePrice;
    public CmdError? Error;
}

internal sealed class OrderResult
{
    public ulong OrderTicket, PositionTicket;
    public double ExecutedPrice, ExecutedVolume;
    public string ClientOrderId = "";
}

internal sealed class AccountInfo
{
    public long Login;
    public string BrokerServer = "", BrokerCompany = "", Currency = "";
    public double Balance, Equity, Margin, MarginFree;
    public double MarginLevel;
    public int Leverage;
    public bool TradeAllowed;
    public string AccountName = "";
    public string Environment = ""; // "demo" | "contest" | "live" | ""
}

/// <summary>Rows accumulated between Fleet_BeginResult and Fleet_EndResult.</summary>
internal sealed class ResultBuilder
{
    public string Id = "";
    public List<Bar> Bars = new();
    public List<Position> Positions = new();
    public List<PendingOrder> Orders = new();
    public List<SymbolInfo> Symbols = new();
    public List<Deal> Deals = new();
    public List<CloseResult> CloseResults = new();
}

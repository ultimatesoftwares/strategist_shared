namespace Core.Trading;

/// <summary>One OHLCV bar. Ts is the bar OPEN time in unix seconds
/// (matches the C++ engine and the NATS/QuestDB contracts).</summary>
public readonly record struct Bar(
    string Symbol, string Tf, long Ts,
    double Open, double High, double Low, double Close, double Volume);

/// <summary>One quote/trade tick; Ts is unix milliseconds.</summary>
public readonly record struct Tick(
    string Symbol, string Venue, long Ts,
    double Bid, double Ask, double Last, double LastSize, long Seq);

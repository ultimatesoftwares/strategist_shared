namespace Core.Trading;

/// <summary>
/// Port of trading::Engine (Engine/src/trading/engine.cpp): turns per-bar
/// stances into position transitions with realized/open PnL. PnL is price
/// units per 1 contract (no qty multiplier), matching the C++ engine; the
/// "+ 0.0" normalizes IEEE -0.0 so serialized output never shows "-0".
/// Lives in the domain so both the engine's OMS and the server's backtest
/// worker replay through the same model.
/// </summary>
public sealed class PositionEngine
{
    public sealed record Trade(long Ts, int Side, double Price, double Pnl);

    private readonly List<Trade> _trades = [];
    private int _lastSignal;
    private bool _ready; // set once the strategy returned a numeric stance

    public int Position { get; private set; }
    public double EntryPrice { get; private set; }
    public double LastPrice { get; private set; }
    // Spread-aware marks: longs exit at bid, shorts at ask. Without a live
    // quote both collapse to the bar close (the old mid-price model).
    public double LastBid { get; private set; }
    public double LastAsk { get; private set; }
    public double RealizedPnl { get; private set; }
    public long Bars { get; private set; }
    public IReadOnlyList<Trade> Trades => _trades;
    public double OpenPnl => Position == 0
        ? 0.0
        : Position * ((Position > 0 ? LastBid : LastAsk) - EntryPrice) + 0.0;
    public double TotalPnl => RealizedPnl + OpenPnl;

    /// <summary>Feeds one bar with the strategy's stance (null = hold the
    /// previous signal). bid/ask, when supplied, price the fills like a real
    /// venue: entries buy at ask / sell at bid, exits the other way around.
    /// Returns the transition trade, if any.</summary>
    public Trade? OnBar(in Bar bar, int? stance, double bid = 0, double ask = 0)
    {
        Bars++;
        LastPrice = bar.Close;
        LastBid = bid > 0 ? bid : bar.Close;
        LastAsk = ask > 0 ? ask : bar.Close;
        if (stance is { } s)
        {
            _lastSignal = Math.Sign(s);
            _ready = true;
        }
        int signal = _lastSignal;
        // A 0 from a warming-up strategy is "no opinion"; from a ready
        // strategy it is an explicit exit to flat.
        if (signal == Position) return null;
        if (signal == 0 && !_ready) return null;

        double exitPrice = Position > 0 ? LastBid : LastAsk;
        double entryPrice = signal > 0 ? LastAsk : signal < 0 ? LastBid : 0.0;
        var trade = new Trade(bar.Ts, signal, signal == 0 ? exitPrice : entryPrice,
            Position != 0 ? Position * (exitPrice - EntryPrice) + 0.0 : 0.0);
        if (Position != 0) RealizedPnl += trade.Pnl;
        Position = signal;
        EntryPrice = entryPrice;
        _trades.Add(trade);
        return trade;
    }

    /// <summary>Risk exit: closes the open position at its exit-side price.</summary>
    public Trade? ForceFlat(long ts)
    {
        if (Position == 0) return null;
        double exitPrice = Position > 0 ? LastBid : LastAsk;
        var trade = new Trade(ts, 0, exitPrice, Position * (exitPrice - EntryPrice) + 0.0);
        RealizedPnl += trade.Pnl;
        Position = 0;
        EntryPrice = 0.0;
        _trades.Add(trade);
        return trade;
    }

}

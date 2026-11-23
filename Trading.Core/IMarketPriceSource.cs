namespace Trading.Core;

/// <summary>Last-trade/indicative prices, distinct from executable bid/ask quotes.</summary>
public interface IMarketPriceSource
{
    event Action<MarketPriceTick>? PriceTick;
}

public readonly record struct MarketPriceTick(
    uint SymbolId, double Price, double? Bid, double? Ask, double? Volume, ulong TsNanos);

/// <summary>Completion of a live feed's background connection loop.</summary>
public interface ILiveFeedLifetime
{
    Task Completion { get; }
}

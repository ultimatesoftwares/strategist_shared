namespace Trading.Core;

/// <summary>
/// One venue-native instrument listing from a broker's catalog, in canonical
/// units (volumes in broker units, not venue wire encodings).
/// </summary>
/// <param name="VenueSymbol">Venue-native symbol ("EURUSD", "BTC/USD").</param>
/// <param name="VenueSymbolId">Venue-native numeric id (cTrader symbolId; 0 when the venue has none).</param>
/// <param name="Description">Human-readable listing description; empty when the venue serves none.</param>
/// <param name="AssetClass">Venue-reported asset class token ("us_equity", "crypto", ...); empty when unknown.</param>
/// <param name="TickSize">Minimum price increment; 0 when unknown.</param>
/// <param name="ContractSize">Canonical-to-venue quantity conversion factor (units per 1.0 lot).</param>
/// <param name="MinQuantity">Smallest order quantity in broker units; 0 when unknown.</param>
/// <param name="MaxQuantity">Largest order quantity in broker units; 0 when unknown.</param>
/// <param name="QuantityStep">Quantity increment in broker units; 0 when unknown.</param>
/// <param name="Currency">Quote/settlement currency; empty when unknown.</param>
/// <param name="Tradable">Whether the venue currently accepts orders for it.</param>
public sealed record InstrumentListing(
    string VenueSymbol,
    long VenueSymbolId,
    string Description,
    string AssetClass,
    double TickSize,
    double ContractSize,
    double MinQuantity,
    double MaxQuantity,
    double QuantityStep,
    string Currency,
    bool Tradable);

/// <summary>
/// Optional broker adapter capability: download the account's full instrument
/// catalog. Called after the handshake (ConnectAsync + subscriptions), so
/// implementations may rely on connection state established there.
/// </summary>
public interface IInstrumentCatalogSource
{
    /// <summary>Canonical venue account key, e.g. "ctrader:1234:demo".</summary>
    string AccountKey { get; }

    /// <summary>Provider segment of the key, e.g. "ctrader" | "metatrader5" | "sim".</summary>
    string Provider { get; }

    /// <summary>Fetch the venue's current instrument catalog.</summary>
    Task<IReadOnlyList<InstrumentListing>> GetInstrumentCatalogAsync(CancellationToken ct = default);
}

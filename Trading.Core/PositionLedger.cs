namespace Trading.Core;

/// <summary>
/// Average-cost position ledger for per-fill realized-PnL attribution.
///
/// Feed it every fill in execution order; it keeps one signed position with a
/// volume-weighted average entry price per key (caller-defined, e.g.
/// "venue:symbol") and answers, for each fill, how much PnL that fill
/// realized: fills that extend the position (or open one) realize nothing and
/// return null; fills that reduce or flip it realize
/// (fill − avg entry) × closed lots, signed by the side of the position being
/// closed. A flip re-opens the remainder at the fill price.
///
/// Values are in price units × lots (the quote currency of the instrument per
/// engine lot) — the same convention as <see cref="EngineHost.RealizedCashFlow"/>.
/// The ledger only knows the fills it was fed: positions opened before the
/// process started (or adjusted by broker-side reconciliation) have no basis
/// here, so their closing fills open a phantom opposite position instead of
/// realizing PnL. Callers should treat the output as per-session attribution,
/// not account-statement truth.
///
/// Not thread-safe; confine to one consumer (it is designed for a
/// single-threaded drain loop, off the trading hot path).
/// </summary>
public sealed class PositionLedger
{
    private sealed class Entry
    {
        public double Lots;
        public double AvgPrice;
    }

    private readonly Dictionary<string, Entry> _positions = new();

    /// <summary>Apply one fill; returns the PnL it realized, or null when it
    /// only opened/extended the position (nothing was realized).</summary>
    public double? Apply(string key, bool isBuy, double qty, double price)
    {
        if (qty <= 0)
            return null;

        var signed = isBuy ? qty : -qty;
        if (!_positions.TryGetValue(key, out var basis))
            _positions[key] = basis = new Entry();

        // Opening or extending: fold the fill into the average entry price.
        if (basis.Lots == 0 || Math.Sign(basis.Lots) == Math.Sign(signed))
        {
            var absLots = Math.Abs(basis.Lots);
            basis.AvgPrice = (basis.AvgPrice * absLots + price * qty) / (absLots + qty);
            basis.Lots += signed;
            return null;
        }

        // Reducing (or flipping): realize the closed lots against the average.
        var closed = Math.Min(Math.Abs(basis.Lots), qty);
        var realized = (price - basis.AvgPrice) * closed * Math.Sign(basis.Lots);
        basis.Lots += signed;
        if (basis.Lots == 0)
            _positions.Remove(key);
        else if (Math.Sign(basis.Lots) == Math.Sign(signed))
            basis.AvgPrice = price; // flipped: remainder opened at the fill price
        return realized;
    }

    /// <summary>Current signed position for a key (0 when flat).</summary>
    public double Position(string key) =>
        _positions.TryGetValue(key, out var basis) ? basis.Lots : 0;

    /// <summary>Current basis for a key: signed lots and volume-weighted average
    /// entry price, or null when flat (flat positions keep no basis).</summary>
    public (double Lots, double AvgPrice)? Basis(string key) =>
        _positions.TryGetValue(key, out var basis) ? (basis.Lots, basis.AvgPrice) : null;
}

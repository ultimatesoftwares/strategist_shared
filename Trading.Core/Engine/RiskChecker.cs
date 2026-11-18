namespace Trading.Core.Engine;

public enum RiskVerdict
{
    Approved,
    RejectedOrderTooLarge,
    RejectedPositionLimit,
    RejectedTooManyOpenOrders,
}

/// <summary>Pre-trade risk checks applied to every command the strategy emits.</summary>
public sealed class RiskChecker(RiskLimits limits)
{
    public RiskLimits Limits { get; } = limits;

    /// <summary>
    /// Validate a command against current position and open-order exposure.
    ///
    /// For placements, worst-case position is current net position plus all
    /// open same-side exposure plus the new order's quantity.
    /// </summary>
    public RiskVerdict Check(
        OrderCommand command, long position, IReadOnlyDictionary<ulong, OpenOrder> openOrders)
    {
        if (command is not OrderCommand.Place place)
            return RiskVerdict.Approved; // Cancels always reduce exposure.

        if (place.Qty > Limits.MaxOrderQty)
            return RiskVerdict.RejectedOrderTooLarge;

        var openForSymbol = 0UL;
        var openSameSide = 0L;
        foreach (var order in openOrders.Values)
        {
            if (order.SymbolId != place.SymbolId)
                continue;
            openForSymbol++;
            if (order.Side == place.Side)
                openSameSide += (long)order.LeavesQty;
        }
        if (openForSymbol >= Limits.MaxOpenOrders)
            return RiskVerdict.RejectedTooManyOpenOrders;

        var signedQty = place.Side == Side.Buy
            ? (long)place.Qty + openSameSide
            : -((long)place.Qty + openSameSide);
        var worstCase = position + signedQty;
        if (Math.Abs(worstCase) > Limits.MaxPosition)
            return RiskVerdict.RejectedPositionLimit;

        return RiskVerdict.Approved;
    }
}

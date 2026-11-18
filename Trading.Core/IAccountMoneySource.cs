namespace Trading.Core;

/// <summary>Broker-reported account money in the deposit currency.
/// Equity is null when the venue does not report it directly.</summary>
public readonly record struct AccountMoney(decimal Balance, decimal? Equity, string Currency);

/// <summary>
/// Read-only account-money capability: the latest broker-reported snapshot,
/// kept current by the adapter (push events or a cached background refresh).
/// Null until the first report after connect. Diagnostics only — never part
/// of an order path, so observe-only decoration is safe to unwrap.
/// </summary>
public interface IAccountMoneySource
{
    AccountMoney? Money { get; }
}

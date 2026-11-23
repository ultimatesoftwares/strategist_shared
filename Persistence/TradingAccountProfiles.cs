using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data
{
    /// <summary>
    /// Account-type vocabulary and the shared upsert for
    /// <see cref="TradingAccountProfile"/> rows.
    /// </summary>
    public static class TradingAccountProfiles
    {
        public const string Netting = "netting";
        public const string Hedging = "hedging";
        public const string SpreadBetting = "spread_betting";

        /// <summary>Provider prefix of a bot account name (<c>ctrader:1:live</c> → <c>ctrader</c>).</summary>
        public static string ProviderOf(string accountKey)
        {
            var i = accountKey.IndexOf(':');
            return (i < 0 ? accountKey : accountKey[..i]).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Account type implied by the venue itself, for providers that only
        /// support one: Alpaca nets, MT4 always hedges. Null when it varies per account.
        /// </summary>
        public static string? ImpliedAccountType(string provider) => provider switch
        {
            "alpaca" => Netting,
            "mt4" => Hedging,
            _ => null,
        };

        /// <summary>
        /// Insert or update the profile. A null <paramref name="accountType"/>
        /// never overwrites a known value. Returns true when the row changed.
        /// </summary>
        public static async Task<bool> UpsertAsync(
            AppDbContext db, string accountKey, string? accountType, string source,
            CancellationToken ct = default)
        {
            var row = await db.TradingAccountProfiles.FindAsync([accountKey], ct);
            if (row is null)
            {
                db.TradingAccountProfiles.Add(new TradingAccountProfile
                {
                    AccountKey = accountKey,
                    Provider = ProviderOf(accountKey),
                    AccountType = accountType,
                    Source = source,
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            }
            else
            {
                if (accountType is null || row.AccountType == accountType) return false;
                row.AccountType = accountType;
                row.Source = source;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return true;
        }
    }
}

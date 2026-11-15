namespace Core.Catalog;

/// <summary>Normalizes venue-native listing symbols so the mapper can SUGGEST
/// catalog candidates for a canonical instrument (spec §7). Suggestions are
/// never auto-bound — a human confirms every mapping via the import endpoints.</summary>
public static class SymbolNormalizer
{
    // Common broker listing suffixes ("EURUSD.m", "XAUUSD.r", "GBPUSD_ecn").
    private static readonly string[] Suffixes =
        [".m", ".r", ".a", ".b", ".c", ".i", ".p", ".x", ".pro", ".ecn", ".raw",
         ".std", ".mini", ".micro", "_m", "_ecn", "_raw", "-ecn"];

    /// <summary>Uppercases, strips known broker suffixes, then drops every
    /// non-alphanumeric character ("EURUSD.m" → "EURUSD", "BTC/USD" → "BTCUSD").</summary>
    public static string Normalize(string symbol)
    {
        var s = symbol.Trim();
        foreach (var suffix in Suffixes)
        {
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^suffix.Length];
                break;
            }
        }
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var c in s)
            if (char.IsLetterOrDigit(c))
                buf[n++] = char.ToUpperInvariant(c);
        return new string(buf[..n]);
    }

    /// <summary>Match score for suggestion ranking: 100 exact (normalized),
    /// 80 candidate extends the canonical symbol (suffix the list above missed),
    /// 60 containment either way, 0 no match.</summary>
    public static int Score(string canonicalSymbol, string venueSymbol)
    {
        var a = Normalize(canonicalSymbol);
        var b = Normalize(venueSymbol);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 100;
        if (b.StartsWith(a, StringComparison.Ordinal)) return 80;
        if (a.StartsWith(b, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal)
            || b.Contains(a, StringComparison.Ordinal)) return 60;
        return 0;
    }
}

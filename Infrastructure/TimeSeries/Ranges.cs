using Strategist.TimeSeries;

namespace Strategist.Infrastructure.TimeSeries;

// Calendar range splitting: the chunk shapes backtests iterate.
public sealed partial class QuestDb
{
    /// <summary>
    /// Splits [from, to) into calendar-aligned chunks (first/last chunks are
    /// clipped to the requested bounds), e.g. 2021-03-10..2024-02-01 by
    /// Year → [2021-03-10..2022), [2022..2023), [2023..2024), [2024..2024-02-01).
    /// Backtests iterate these instead of loading years of bars at once.
    /// </summary>
    public static List<DateRange> SplitRange(DateTime from, DateTime to, RangeUnit unit)
    {
        from = AsUtc(from); to = AsUtc(to);
        var ranges = new List<DateRange>();
        if (to <= from) return ranges;
        var cursor = from;
        while (cursor < to)
        {
            var next = NextBoundary(cursor, unit);
            if (next > to) next = to;
            ranges.Add(new DateRange(cursor, next));
            cursor = next;
        }
        return ranges;
    }

    /// <summary>Splits a range the same way, from a DateRange.</summary>
    public static List<DateRange> SplitRange(DateRange range, RangeUnit unit) =>
        SplitRange(range.From, range.To, unit);

    private static DateTime NextBoundary(DateTime t, RangeUnit unit) => unit switch
    {
        RangeUnit.Day => t.Date.AddDays(1),
        RangeUnit.Week => t.Date.AddDays(7 - ((int)t.DayOfWeek + 6) % 7), // next Monday
        RangeUnit.Month => new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
        RangeUnit.Year => new DateTime(t.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };
}

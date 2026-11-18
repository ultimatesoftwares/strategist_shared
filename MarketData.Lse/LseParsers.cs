using System.Globalization;

using Parquet;
using Parquet.Schema;

namespace MarketData.Lse;

/// <summary>
/// Decoders of LSE export files. Candles are `time, open, high, low, close,
/// volume, trades` (daily and longer carry a trailing `date` column); the
/// trade slice is `time, price, size, kind`. CSV is RFC-4180-ish with quoted
/// fields; Parquet types `size` (and split-adjusted volumes) as double.
/// </summary>
internal static class LseParsers
{
    // ---- CSV ----

    public static List<LseCandle> ParseCandlesCsv(ReadOnlySpan<byte> bytes)
    {
        var rows = new List<LseCandle>();
        foreach (var fields in CsvRows(bytes, out var col))
            rows.Add(new LseCandle(
                ParseUtcTime(fields[col["time"]]),
                ParseDouble(fields[col["open"]]),
                ParseDouble(fields[col["high"]]),
                ParseDouble(fields[col["low"]]),
                ParseDouble(fields[col["close"]]),
                ParseDouble(fields[col["volume"]]),
                (long)ParseDouble(fields[col["trades"]])));
        return rows;
    }

    public static List<LseTrade> ParseTradesCsv(ReadOnlySpan<byte> bytes)
    {
        var rows = new List<LseTrade>();
        foreach (var fields in CsvRows(bytes, out var col))
            rows.Add(new LseTrade(
                ParseUtcTime(fields[col["time"]]),
                ParseDouble(fields[col["price"]]),
                ParseDouble(fields[col["size"]]),
                col.TryGetValue("kind", out var k) && k < fields.Count ? fields[k] : ""));
        return rows;
    }

    /// <summary>Parses the CSV into rows of fields, mapping the header into
    /// <paramref name="columns"/> (lower-cased name to index).</summary>
    private static List<List<string>> CsvRows(ReadOnlySpan<byte> bytes, out Dictionary<string, int> columns)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var records = ParseCsv(text);
        columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (records.Count == 0)
            return records;
        var header = records[0];
        for (var i = 0; i < header.Count; i++)
            columns[header[i].Trim()] = i;
        records.RemoveAt(0);
        return records;
    }

    /// <summary>A minimal CSV reader: quoted fields, doubled quotes, CRLF or
    /// LF row ends. Skips blank rows.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (any || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        records.Add(fields);
                        fields = [];
                        field.Clear();
                        any = false;
                    }
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }
        if (any || field.Length > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }
        return records;
    }

    // ---- Parquet ----

    public static async Task<List<LseCandle>> ParseCandlesParquetAsync(
        byte[] bytes, CancellationToken ct = default)
    {
        var rows = new List<LseCandle>();
        await ReadParquetAsync(bytes,
            ["time", "open", "high", "low", "close", "volume", "trades"],
            cells => rows.Add(new LseCandle(
                ToUtcTime(cells[0]), ToDouble(cells[1]), ToDouble(cells[2]),
                ToDouble(cells[3]), ToDouble(cells[4]), ToDouble(cells[5]),
                (long)ToDouble(cells[6]))),
            ct);
        return rows;
    }

    public static async Task<List<LseTrade>> ParseTradesParquetAsync(
        byte[] bytes, CancellationToken ct = default)
    {
        var rows = new List<LseTrade>();
        await ReadParquetAsync(bytes,
            ["time", "price", "size", "kind"],
            cells => rows.Add(new LseTrade(
                ToUtcTime(cells[0]), ToDouble(cells[1]), ToDouble(cells[2]),
                cells[3]?.ToString() ?? "")),
            ct);
        return rows;
    }

    /// <summary>Reads the named columns of every row group and hands each row
    /// to <paramref name="onRow"/> as cells in the order of <paramref name="names"/>
    /// (a missing optional column yields nulls).</summary>
    private static async Task ReadParquetAsync(
        byte[] bytes, string[] names, Action<object?[]> onRow, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct);
        var schema = reader.Schema.GetDataFields();
        var fields = new DataField?[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            fields[i] = schema.FirstOrDefault(
                f => string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase));
            if (fields[i] is null && i == 0)
                throw new LseException(0, "parquet-schema",
                    $"the Parquet file has no '{names[i]}' column (columns: {string.Join(", ", schema.Select(f => f.Name))})");
        }
        var cells = new object?[names.Length];
        for (var g = 0; g < reader.RowGroupCount; g++)
        {
            using var group = reader.OpenRowGroupReader(g);
            var columns = new Array?[names.Length];
            var count = 0;
            for (var i = 0; i < names.Length; i++)
            {
                if (fields[i] is not { } field)
                    continue;
                var column = await group.ReadColumnAsync(field, ct);
                columns[i] = column.Data;
                count = Math.Max(count, column.Data.Length);
            }
            for (var row = 0; row < count; row++)
            {
                for (var i = 0; i < names.Length; i++)
                    cells[i] = columns[i] is { } data && row < data.Length ? data.GetValue(row) : null;
                onRow(cells);
            }
        }
    }

    // ---- Cell conversions ----

    internal static DateTime ToUtcTime(object? value) => value switch
    {
        DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        string s => ParseUtcTime(s),
        null => throw new LseException(0, "parquet-schema", "a row has no time"),
        _ => throw new LseException(0, "parquet-schema",
            $"cannot read a {value.GetType().Name} as a time"),
    };

    internal static double ToDouble(object? value) => value switch
    {
        null => 0,
        double d => d,
        float f => f,
        decimal m => (double)m,
        string s => ParseDouble(s),
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };

    internal static double ParseDouble(string s) =>
        s.Length == 0 || s == @"\N"
            ? 0
            : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the server's UTC times: "yyyy-MM-dd HH:mm:ss" with an optional
    /// fraction of up to nanoseconds (.NET keeps 100 ns ticks; the rest is
    /// truncated), an optional 'T' separator and an optional trailing 'Z'.
    /// A bare date is midnight.
    /// </summary>
    internal static DateTime ParseUtcTime(string s)
    {
        s = s.Trim();
        if (s.EndsWith('Z'))
            s = s[..^1];
        if (s.Length > 10 && s[10] == 'T')
            s = s[..10] + " " + s[11..];
        var fractionTicks = 0L;
        var dot = s.IndexOf('.');
        if (dot >= 0)
        {
            var fraction = s[(dot + 1)..];
            s = s[..dot];
            if (fraction.Length > 7)
                fraction = fraction[..7];
            fractionTicks = long.Parse(fraction, CultureInfo.InvariantCulture)
                * (long)Math.Pow(10, 7 - fraction.Length);
        }
        var baseTime = DateTime.ParseExact(s,
            s.Length == 10 ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return baseTime.AddTicks(fractionTicks);
    }
}

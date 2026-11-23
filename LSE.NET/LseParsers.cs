using System.Globalization;

using Parquet;
using Parquet.Schema;

namespace LSE.NET;

/// <summary>
/// Decoders of LSE export files. Candles are `time, open, high, low, close,
/// volume, trades` (daily and longer carry a trailing `date` column); the
/// trade slice is `time, price, size, kind`. CSV is RFC-4180-ish with quoted
/// fields; Parquet types `size` (and split-adjusted volumes) as double.
/// </summary>
internal static class LseParsers
{
    // ---- CSV ----

    public static List<LseCandle> ParseCandlesCsv(ReadOnlySpan<byte> bytes, CancellationToken ct = default)
    {
        var rows = new List<LseCandle>();
        foreach (var fields in CsvRows(bytes, ["time", "open", "high", "low", "close", "volume", "trades"], ct, out var col))
            rows.Add(new LseCandle(
                ParseUtcTime(fields[col["time"]]),
                ParseDouble(fields[col["open"]]),
                ParseDouble(fields[col["high"]]),
                ParseDouble(fields[col["low"]]),
                ParseDouble(fields[col["close"]]),
                ParseDouble(fields[col["volume"]]),
                long.Parse(fields[col["trades"]], CultureInfo.InvariantCulture)));
        return rows;
    }

    public static List<LseTrade> ParseTradesCsv(ReadOnlySpan<byte> bytes, CancellationToken ct = default)
    {
        var rows = new List<LseTrade>();
        foreach (var fields in CsvRows(bytes, ["time", "price", "size"], ct, out var col))
            rows.Add(new LseTrade(
                ParseUtcTime(fields[col["time"]]),
                ParseDouble(fields[col["price"]]),
                ParseDouble(fields[col["size"]]),
                col.TryGetValue("kind", out var k) ? fields[k] : ""));
        return rows;
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, string?>> ParseRowsCsv(ReadOnlySpan<byte> bytes,
        CancellationToken ct)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var fields in CsvRows(bytes, [], ct, out var columns))
            rows.Add(columns.ToDictionary(column => column.Key, column =>
                fields[column.Value] is "" or @"\N" ? null : fields[column.Value], StringComparer.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>Parses the CSV into rows of fields, mapping the header into
    /// <paramref name="columns"/> (lower-cased name to index).</summary>
    private static IEnumerable<List<string>> CsvRows(ReadOnlySpan<byte> bytes, string[] required,
        CancellationToken ct, out Dictionary<string, int> columns)
    {
        columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return ReadRows(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), columns, required, ct);
    }

    private static IEnumerable<List<string>> ReadRows(string text, Dictionary<string, int> columns,
        string[] required, CancellationToken ct)
    {
        using var records = EnumerateCsv(text, ct).GetEnumerator();
        if (!records.MoveNext())
            throw new LseException(0, "csv-schema", "The CSV has no header.");
        var header = records.Current;
        for (var i = 0; i < header.Count; i++)
            if (!columns.TryAdd(header[i].Trim(), i))
                throw new LseException(0, "csv-schema", "The CSV repeats a column name.");
        if (required.Any(name => !columns.ContainsKey(name)))
            throw new LseException(0, "csv-schema", "The CSV is missing a required column.");
        while (records.MoveNext())
        {
            if (records.Current.Count != header.Count)
                throw new LseException(0, "csv-schema", "A CSV row does not match the header.");
            yield return records.Current;
        }
    }

    private static IEnumerable<List<string>> EnumerateCsv(string text, CancellationToken ct)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var closedQuote = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0)
                ct.ThrowIfCancellationRequested();
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closedQuote = true; }
                }
                else field.Append(c);
                continue;
            }
            if (closedQuote && c is not (',' or '\r' or '\n'))
                throw new LseException(0, "csv-schema", "Unexpected data after a quoted CSV field.");
            switch (c)
            {
                case '"':
                    if (field.Length > 0)
                        throw new LseException(0, "csv-schema", "Unexpected quote in a CSV field.");
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    closedQuote = false;
                    any = true;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    goto case '\n';
                case '\n':
                    if (any || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        yield return fields;
                        fields = [];
                        field.Clear();
                        any = false;
                        closedQuote = false;
                    }
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }
        if (quoted)
            throw new LseException(0, "csv-schema", "The CSV ends inside a quoted field.");
        if (any || field.Length > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }

    // ---- Parquet ----

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
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var schema = reader.Schema.GetDataFields();
        var fields = new DataField?[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            fields[i] = schema.FirstOrDefault(
                f => string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase));
            if (fields[i] is null && names[i] != "kind")
                throw new LseException(0, "parquet-schema",
                    $"the Parquet file has no '{names[i]}' column (columns: {string.Join(", ", schema.Select(f => f.Name))})");
        }
        var cells = new object?[names.Length];
        for (var g = 0; g < reader.RowGroupCount; g++)
        {
            using var group = reader.OpenRowGroupReader(g);
            var columns = new Array?[names.Length];
            var count = checked((int)group.RowCount);
            for (var i = 0; i < names.Length; i++)
            {
                if (fields[i] is not { } field)
                    continue;
                var column = await group.ReadColumnAsync(field, ct).ConfigureAwait(false);
                columns[i] = column.Data;
                if (column.Data.Length != count)
                    throw new LseException(0, "parquet-schema", "A Parquet column does not match the row group length.");
            }
            for (var row = 0; row < count; row++)
            {
                if ((row & 4095) == 0)
                    ct.ThrowIfCancellationRequested();
                for (var i = 0; i < names.Length; i++)
                    cells[i] = columns[i]?.GetValue(row);
                onRow(cells);
            }
        }
    }

    // ---- Cell conversions ----

    private static DateTime ToUtcTime(object? value) => value switch
    {
        DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        string s => ParseUtcTime(s),
        null => throw new LseException(0, "parquet-schema", "a row has no time"),
        _ => throw new LseException(0, "parquet-schema",
            $"cannot read a {value.GetType().Name} as a time"),
    };

    private static double ToDouble(object? value) => value switch
    {
        null => 0,
        double d => d,
        float f => f,
        decimal m => (double)m,
        string s => ParseDouble(s),
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };

    private static double ParseDouble(string s) =>
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
        else if (s.EndsWith("+00:00", StringComparison.Ordinal))
            s = s[..^6];
        if (s.Length > 10 && s[10] == 'T')
            s = s[..10] + " " + s[11..];
        var fractionTicks = 0L;
        var dot = s.IndexOf('.');
        if (dot >= 0)
        {
            var fraction = s[(dot + 1)..];
            if (fraction.Length is < 1 or > 9 || !fraction.All(char.IsAsciiDigit))
                throw new FormatException("Invalid fractional timestamp.");
            s = s[..dot];
            if (fraction.Length > 7)
                fraction = fraction[..7];
            fractionTicks = long.Parse(fraction, NumberStyles.None, CultureInfo.InvariantCulture)
                * (long)Math.Pow(10, 7 - fraction.Length);
        }
        var baseTime = DateTime.ParseExact(s,
            s.Length == 10 ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return baseTime.AddTicks(fractionTicks);
    }
}

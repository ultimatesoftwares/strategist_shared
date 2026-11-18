using System.Globalization;

namespace Trading.Core;

/// <summary>One L2 delta in a historical data file. Qty 0 removes the level.</summary>
public readonly record struct MarketDataRecord(
    ulong TsNanos, string Symbol, Side Side, long Price, ulong Qty);

/// <summary>
/// Streaming CSV reader/writer for historical L2 data. Produced by live
/// recording (<see cref="MarketDataRecorder"/>) and synthetic generation,
/// consumed by the backtest engine.
///
/// Format (header required):
///   ts_nanos,symbol,side,price,qty
///   1,EURUSD,B,109876,25
///   2,EURUSD,S,109881,40
///
/// `side` is B (bid) or S (ask); `price` is a fixed-point scaled integer
/// (the file's scale is a convention between producer and consumer);
/// `qty` 0 removes the price level.
/// </summary>
public static class MarketDataFile
{
    public const string Header = "ts_nanos,symbol,side,price,qty";

    public static IEnumerable<MarketDataRecord> Read(string path)
    {
        using var reader = new StreamReader(path);
        var header = reader.ReadLine();
        if (header is null || !header.StartsWith("ts_nanos", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{path}: expected header '{Header}'.");

        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
                continue;

            var parts = line.Split(',');
            if (parts.Length != 5)
                throw new InvalidDataException($"{path}:{lineNumber}: expected 5 fields, got {parts.Length}.");

            yield return new MarketDataRecord(
                ulong.Parse(parts[0], CultureInfo.InvariantCulture),
                parts[1],
                parts[2] switch
                {
                    "B" or "b" => Side.Buy,
                    "S" or "s" => Side.Sell,
                    _ => throw new InvalidDataException($"{path}:{lineNumber}: side must be B or S."),
                },
                long.Parse(parts[3], CultureInfo.InvariantCulture),
                ulong.Parse(parts[4], CultureInfo.InvariantCulture));
        }
    }

    public static void Write(string path, IEnumerable<MarketDataRecord> records)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(Header);
        foreach (var record in records)
            WriteRecord(writer, record);
    }

    /// <summary>Append a single CSV data line (no header).</summary>
    public static void WriteRecord(TextWriter writer, in MarketDataRecord r)
    {
        writer.Write(r.TsNanos.ToString(CultureInfo.InvariantCulture));
        writer.Write(',');
        writer.Write(r.Symbol);
        writer.Write(',');
        writer.Write(r.Side == Side.Buy ? 'B' : 'S');
        writer.Write(',');
        writer.Write(r.Price.ToString(CultureInfo.InvariantCulture));
        writer.Write(',');
        writer.WriteLine(r.Qty.ToString(CultureInfo.InvariantCulture));
    }
}

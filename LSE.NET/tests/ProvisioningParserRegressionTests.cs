using LSE.NET;
using Xunit;

namespace LSE.NET.Tests;

public class ProvisioningParserRegressionTests
{
    [Fact]
    public void ParsesCandlesCsvWithQuotedHeaderAndTrailingDateColumn()
    {
        const string csv =
            "\"time\",\"open\",\"high\",\"low\",\"close\",\"volume\",\"trades\",\"date\"\n" +
            "\"2026-10-01 04:02:00\",334.18,334.83,325.82,330.79,15666143,265910,\"2026-10-01\"\n" +
            "\"2026-10-02 04:00:00\",331.14,334.54,330.17,333.53,15026716,228816,\"2026-10-02\"\n";
        var rows = ParseCandles(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2026, 10, 1, 4, 2, 0, DateTimeKind.Utc), rows[0].Time);
        Assert.Equal(DateTimeKind.Utc, rows[0].Time.Kind);
        Assert.Equal(334.18, rows[0].Open);
        Assert.Equal(330.79, rows[0].Close);
        Assert.Equal(265910, rows[0].Trades);
        Assert.Equal(15026716, rows[1].Volume);
    }

    [Fact]
    public void ParsesTradesCsvWithNanosecondTimes()
    {
        const string csv =
            "time,price,size,kind\n" +
            "2026-10-21 13:34:17.213383276,239.25,6,trade\n";
        var rows = ParseTrades(csv);

        var trade = Assert.Single(rows);
        Assert.Equal(239.25, trade.Price);
        Assert.Equal(6, trade.Size);
        Assert.Equal("trade", trade.Kind);
        // Nanoseconds truncate to 100 ns ticks: .213383276 -> .2133832
        var expected = new DateTime(2026, 10, 21, 13, 34, 17, DateTimeKind.Utc).AddTicks(2133832);
        Assert.Equal(expected, trade.Time);
    }

    [Theory]
    [InlineData("2026-10-22 23:10:00")]
    [InlineData("2026-10-22T23:10:00")]
    [InlineData("2026-10-22T23:10:00Z")]
    public void ParseUtcTimeAcceptsTheServersSpellings(string spelling)
    {
        var time = LseParsers.ParseUtcTime(spelling);
        Assert.Equal(new DateTime(2026, 10, 22, 23, 10, 0, DateTimeKind.Utc), time);
        Assert.Equal(DateTimeKind.Utc, time.Kind);
    }

    [Fact]
    public void ParseUtcTimeAcceptsABareDateAsMidnight()
    {
        Assert.Equal(
            new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
            LseParsers.ParseUtcTime("2026-10-22"));
    }

    [Fact]
    public void ParseDoubleTreatsNullMarkerAsZero()
    {
        var rows = ParseTrades("time,price,size\n2026-10-22,1.5,\\N\n2026-10-22,1.5,\n");
        Assert.All(rows, row =>
        {
            Assert.Equal(1.5, row.Price);
            Assert.Equal(0, row.Size);
        });
    }

    [Fact]
    public void CsvReaderHandlesQuotesCommasAndCrlf()
    {
        var record = Assert.Single(LseParsers.ParseRowsCsv("a,b\r\n\"x,\"\"y\"\"\",2\r\n"u8, default));
        Assert.Equal("x,\"y\"", record["a"]);
        Assert.Equal("2", record["b"]);
    }

    [Fact]
    public async Task ParquetTradesRoundTrip()
    {
        var times = new[]
        {
            new DateTime(2026, 10, 21, 13, 34, 17, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 13, 34, 18, DateTimeKind.Utc),
        };
        var schema = new Parquet.Schema.ParquetSchema(
            new Parquet.Schema.DataField<DateTime>("time"),
            new Parquet.Schema.DataField<double>("price"),
            new Parquet.Schema.DataField<double>("size"),
            new Parquet.Schema.DataField<string>("kind"));
        using var stream = new MemoryStream();
        using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
        {
            using var group = writer.CreateRowGroup();
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(
                (Parquet.Schema.DataField)schema[0], times));
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(
                (Parquet.Schema.DataField)schema[1], new[] { 239.25, 239.30 }));
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(
                (Parquet.Schema.DataField)schema[2], new[] { 6.0, 12.0 }));
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(
                (Parquet.Schema.DataField)schema[3], new[] { "trade", "trade" }));
        }

        var rows = await LseParsers.ParseTradesParquetAsync(stream.ToArray());

        Assert.Equal(2, rows.Count);
        Assert.Equal(times[0], rows[0].Time);
        Assert.Equal(239.25, rows[0].Price);
        Assert.Equal(12.0, rows[1].Size);
        Assert.Equal("trade", rows[1].Kind);
    }

    private static List<LseCandle> ParseCandles(string csv) =>
        LseParsers.ParseCandlesCsv(System.Text.Encoding.UTF8.GetBytes(csv));

    private static List<LseTrade> ParseTrades(string csv) =>
        LseParsers.ParseTradesCsv(System.Text.Encoding.UTF8.GetBytes(csv));
}

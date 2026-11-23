using LSE.NET;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Lse.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ReadsBomReorderedHeadersAndQuotedMultilineFields()
    {
        var trades = LseParsers.ParseTradesCsv("\uFEFFprice,time,size,kind\r\n12.5,2026-10-23,1,\"odd \"\"lot\"\"\r\nsecond line\""u8);
        Assert.Equal("odd \"lot\"\r\nsecond line", Assert.Single(trades).Kind);
    }

    [Fact]
    public void DistinguishesEmptyDataFromMalformedCsv()
    {
        Assert.Empty(LseParsers.ParseTradesCsv("time,price,size\n"u8));
        Assert.Throws<LseException>(() => LseParsers.ParseTradesCsv("time,price\n"u8));
        Assert.Throws<LseException>(() => LseParsers.ParseTradesCsv("time,price,size\n2026-10-23,12\n"u8));
        Assert.Throws<LseException>(() => LseParsers.ParseTradesCsv("time,price,size,kind\n2026-10-23,12,1,\"unclosed"u8));
    }

    [Fact]
    public async Task ReadsParquetRowGroupsAndAllowsMissingKind()
    {
        var time = new DataField<string>("time");
        var price = new DataField<double>("price");
        var size = new DataField<double>("size");
        using var stream = new MemoryStream();
        using (var writer = await ParquetWriter.CreateAsync(new ParquetSchema(time, price, size), stream))
        {
            for (var i = 0; i < 2; i++)
            {
                using var group = writer.CreateRowGroup();
                await group.WriteColumnAsync(new DataColumn(time, new[] { $"2026-10-23T12:00:0{i}.123456789Z" }));
                await group.WriteColumnAsync(new DataColumn(price, new[] { 10.1 + i }));
                await group.WriteColumnAsync(new DataColumn(size, new[] { .25 }));
            }
        }
        var rows = await LseParsers.ParseTradesParquetAsync(stream.ToArray());
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[1].Time.Second);
        Assert.Equal(.25, rows[1].Size);
        Assert.Empty(rows[1].Kind);
    }
}

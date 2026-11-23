using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using LSE.NET;
using Microsoft.AspNetCore.Http;

namespace Lse.Tests;

public sealed class ExchangeStreamTests
{
    [Theory]
    [InlineData(LseExchangeEncoding.Json)]
    [InlineData(LseExchangeEncoding.Dbn)]
    public async Task SendsUpgradeKeyAndReadsSnapshotAndLiveRecords(LseExchangeEncoding encoding)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var encodingName = encoding == LseExchangeEncoding.Json ? "json" : "dbn";
        await using var server = await StreamTests.StartAsync(async (ws, ct) =>
        {
            var sub = await StreamTests.Receive(ws, ct);
            Assert.Equal("subscribe", sub.GetProperty("op").GetString());
            Assert.Equal("ESZ6", sub.GetProperty("symbols")[0].GetString());
            Assert.Equal(2, sub.GetProperty("level").GetInt32());
            Assert.Equal(encodingName, sub.GetProperty("encoding").GetString());
            await StreamTests.Send(ws, $$"""{"type":"hello","level":2,"encoding":"{{encodingName}}","epoch":7}""", ct);
            await StreamTests.Send(ws, """{"type":"snapshot_begin","symbol":"ESZ6","sequence":41}""", ct);
            if (encoding == LseExchangeEncoding.Dbn)
            {
                var bytes = BinaryBook();
                await ws.SendAsync(bytes.AsMemory(0, 12), WebSocketMessageType.Binary, false, ct);
                await ws.SendAsync(bytes.AsMemory(12), WebSocketMessageType.Binary, true, ct);
            }
            else
                await StreamTests.Send(ws, """{"type":"mbp10","sequence":42,"symbol":"ESZ6","record":{"price":"7570000000000"}}""", ct);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        }, deadline.Token, context =>
        {
            Assert.Equal("fixture-exchange", context.Request.Headers["X-API-Key"].ToString());
            Assert.False(context.Request.QueryString.HasValue);
        });
        var endpoint = new UriBuilder(server.Urls.Single()) { Scheme = "ws", Path = "/v1/stream" }.Uri;
        await using var stream = await LseExchangeStream.ConnectAsync(new()
            { ApiKey = "fixture-exchange", Endpoint = endpoint }, new(["ESZ6"], LseBookLevel.PriceLevels, encoding), deadline.Token);
        Assert.Equal(7UL, stream.Hello.Epoch);
        var messages = new List<LseExchangeMessage>();
        await foreach (var message in stream.ReadMessagesAsync(deadline.Token))
            messages.Add(message);
        Assert.Equal(2, messages.Count);
        Assert.Equal("snapshot_begin", Assert.IsType<LseExchangeJsonMessage>(messages[0]).Type);
        if (encoding == LseExchangeEncoding.Dbn)
        {
            var batch = Assert.IsType<LseExchangeDbnBatch>(messages[1]);
            Assert.Equal(42UL, batch.Sequence);
            Assert.Equal(123u, batch.InstrumentId);
            Assert.Equal(368, batch.GetRecord(0).Length);
            Assert.Equal(91, batch.GetRecord(0).Span[0]);
        }
        else
        {
            var json = Assert.IsType<LseExchangeJsonMessage>(messages[1]);
            Assert.Equal("mbp10", json.Type);
            Assert.Equal(42UL, json.Sequence);
            Assert.Equal(7570m, LseExchangeJsonMessage.ParsePrice(json.Record!.Value.GetProperty("price").GetString()!));
            Assert.Null(LseExchangeJsonMessage.ParsePrice("9223372036854775807"));
        }
    }

    [Fact]
    public async Task PreservesSubscriptionCloseCodeAndRedactsTheKey()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await StreamTests.StartAsync(async (ws, ct) =>
        {
            await StreamTests.Receive(ws, ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "symbol unavailable; key fixture-exchange", ct);
        }, deadline.Token);
        var endpoint = new UriBuilder(server.Urls.Single()) { Scheme = "ws" }.Uri;
        var error = await Assert.ThrowsAsync<LseException>(() => LseExchangeStream.ConnectAsync(new()
            { ApiKey = "fixture-exchange", Endpoint = endpoint }, new(["ESZ6"]), deadline.Token));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, error.WebSocketCloseStatus);
        Assert.Contains("symbol unavailable", error.Message);
        Assert.DoesNotContain("fixture-exchange", error.Message);
    }

    [Theory]
    [InlineData(401, "key-unknown")]
    [InlineData(302, "http-302")]
    public async Task SurfacesUpgradeRefusalsWithoutFollowingRedirects(int status, string code)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requests = 0;
        await using var server = await StreamTests.StartAsync((_, _) => Task.CompletedTask, deadline.Token,
            beforeUpgrade: context =>
            {
                Interlocked.Increment(ref requests);
                context.Response.StatusCode = status;
                if (status == (int)HttpStatusCode.Redirect)
                    context.Response.Headers.Location = "/redirected";
                return context.Response.CompleteAsync();
            });
        var endpoint = new UriBuilder(server.Urls.Single()) { Scheme = "ws" }.Uri;
        var error = await Assert.ThrowsAsync<LseException>(() => LseExchangeStream.ConnectAsync(new()
            { ApiKey = "fixture-exchange", Endpoint = endpoint }, new(["ESZ6"]), deadline.Token));
        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void RejectsMismatchedDbnRecordCount()
    {
        var data = BinaryBook();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 2);
        Assert.Equal("stream-protocol", Assert.Throws<LseException>(
            () => LseExchangeStream.ReadDbn(data, LseBookLevel.PriceLevels)).Code);
    }

    private static byte[] BinaryBook()
    {
        var data = new byte[24 + 368];
        "L3DB"u8.CopyTo(data);
        data[4] = 1;
        data[5] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), 42UL);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 123);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1);
        data[24] = 91;
        return data;
    }
}

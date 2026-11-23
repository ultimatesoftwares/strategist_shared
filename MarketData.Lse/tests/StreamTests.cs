using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LSE.NET;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lse.Tests;

public sealed class StreamTests
{
    [Fact]
    public async Task AuthenticatesSubscribesParsesFragmentsAndSendsHeartbeat()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartAsync(async (ws, token) =>
        {
            await Send(ws, """{"type":"welcome"}""", token);
            var auth = await Receive(ws, token);
            Assert.Equal("auth", auth.GetProperty("action").GetString());
            Assert.Equal("fake-stream-key", auth.GetProperty("api_key").GetString());
            await Send(ws, """{"type":"authenticated","tier":"free"}""", token);
            var seen = new HashSet<string>();
            while (seen.Count < 2)
            {
                var command = await Receive(ws, token);
                var action = command.GetProperty("action").GetString();
                if (action == "ping")
                {
                    seen.Add("ping");
                    await Send(ws, """{"type":"pong"}""", token);
                    heartbeat.TrySetResult();
                }
                else
                {
                    Assert.Equal("subscribe", action);
                    Assert.Equal("EUR/USD", command.GetProperty("symbol").GetString());
                    seen.Add("subscribe");
                    await Send(ws, """{"type":"subscribed","symbol":"EUR/USD"}""", token);
                    await Send(ws, """{"type":"tick","symbol":"EUR/""", token, false);
                    await Send(ws, """USD","price":1.12,"ts":"2026-10-23 20:59:05.029000123+00:00","seq":50,"ts_ns":1792789145029000123}""", token);
                }
            }
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, token);
        }, ct);
        await using var feed = await LseStreamClient.ConnectAsync(Options(server, pingMs: 100), ct);
        Assert.Equal("free", feed.Authentication.Payload.GetProperty("tier").GetString());
        await feed.SubscribeAsync("EUR/USD", ct);
        var messages = new List<LseStreamMessage>();
        await foreach (var msg in feed.ReadMessagesAsync(ct))
            messages.Add(msg);
        await heartbeat.Task.WaitAsync(ct);
        Assert.Contains(messages, m => m.Type == "subscribed");
        Assert.Contains(messages, m => m.Type == "pong");
        var tick = Assert.Single(messages, m => m.Type == "tick").GetTick()!.Value;
        Assert.Equal("EUR/USD", tick.Symbol);
        Assert.Equal(50L, tick.Sequence);
        Assert.Equal(1792789145029000123L, tick.TimestampNanoseconds);
        Assert.Null(tick.Bid);
        Assert.Equal(DateTimeKind.Utc, tick.Time.Kind);
    }

    [Fact]
    public async Task SurfacesAuthRefusalWithoutEchoingTheKey()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await StartAsync(async (ws, ct) =>
        {
            await Send(ws, """{"type":"welcome"}""", ct);
            await Receive(ws, ct);
            await Send(ws, """{"type":"error","code":"INVALID_KEY","message":"Refused fake-stream-key"}""", ct);
        }, deadline.Token);
        var refusal = await Assert.ThrowsAsync<LseException>(
            () => LseStreamClient.ConnectAsync(Options(server), deadline.Token));
        Assert.Equal("INVALID_KEY", refusal.Code);
        Assert.DoesNotContain("fake-stream-key", refusal.Message);
    }

    [Fact]
    public async Task RejectsFragmentedMessagesExceedingLimit()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await StartAsync(async (ws, ct) =>
        {
            await Send(ws, """{"type":"welcome"}""", ct);
            await Receive(ws, ct);
            await Send(ws, """{"type":"authenticated"}""", ct);
            await Send(ws, new string('a', 45), ct, false);
            await Send(ws, new string('b', 45), ct);
        }, deadline.Token);
        await using var feed = await LseStreamClient.ConnectAsync(Options(server, maximum: 64), deadline.Token);
        await using var reader = feed.ReadMessagesAsync(deadline.Token).GetAsyncEnumerator();
        Assert.Equal("stream-message-too-large", (await Assert.ThrowsAsync<LseException>(
            () => reader.MoveNextAsync().AsTask())).Code);
    }

    [Fact]
    public async Task ReaderCancellationTerminatesTheConnection()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await StartAsync(async (ws, ct) =>
        {
            await Send(ws, """{"type":"welcome"}""", ct);
            await Receive(ws, ct);
            await Send(ws, """{"type":"authenticated"}""", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, deadline.Token);
        await using var feed = await LseStreamClient.ConnectAsync(Options(server), deadline.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await using var reader = feed.ReadMessagesAsync(cancel.Token).GetAsyncEnumerator();
        var pending = reader.MoveNextAsync().AsTask();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(deadline.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => feed.SubscribeAsync("AAPL", deadline.Token));
        deadline.Cancel();
    }

    private static LseStreamOptions Options(WebApplication server, int pingMs = 25000, int maximum = 1024) => new()
    {
        ApiKey = "fake-stream-key",
        Endpoint = new UriBuilder(server.Urls.Single()) { Scheme = "ws" }.Uri,
        PingInterval = TimeSpan.FromMilliseconds(pingMs),
        MaxMessageBytes = maximum
    };

    internal static async Task<WebApplication> StartAsync(Func<WebSocket, CancellationToken, Task> script, CancellationToken ct,
        Action<HttpContext>? inspectUpgrade = null, Func<HttpContext, Task>? beforeUpgrade = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(async context =>
        {
            inspectUpgrade?.Invoke(context);
            if (beforeUpgrade is not null)
            {
                await beforeUpgrade(context);
                return;
            }
            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, context.RequestAborted);
            await script(ws, linked.Token);
        });
        await app.StartAsync(ct);
        return app;
    }

    internal static Task Send(WebSocket ws, string text, CancellationToken ct, bool end = true) =>
        ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, end, ct);

    internal static async Task<JsonElement> Receive(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<JsonElement>(message.ToArray());
    }
}

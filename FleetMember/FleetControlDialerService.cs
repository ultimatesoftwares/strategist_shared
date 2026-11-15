using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Strategist.FleetControl;
using FleetControlRpc = Strategist.FleetControl.FleetControl;

namespace Strategist.FleetMember;

/// <summary>
/// The member end of the fleet reverse-dial command plane: dials the master
/// server's gRPC listener (Server:GrpcUrl; default = Server:BaseUrl's host
/// on :50052, h2c) and holds one FleetControl.Attach stream open — hello,
/// then a beat every few seconds (the ack round-trip is the next beat's
/// RTT), answering the server's HTTP-shaped commands by executing them
/// against this member's own LOCAL control listener (Fleet:LocalControlUrl;
/// default = the advertised Fleet:ControlUrl). With the stream attached the
/// server never dials this host — a fleet host needs zero inbound ports.
/// The stream is the sole membership path (the REST heartbeat is retired);
/// redials quietly forever when the server is unreachable.
/// </summary>
public sealed class FleetControlDialerService(
    FleetMemberOptions options,
    IConfiguration config,
    IHttpClientFactory httpFactory,
    IEnumerable<IFleetTunnelHandler> tunnelHandlers,
    ILogger<FleetControlDialerService> logger) : BackgroundService
{
    /// <summary>Beat cadence; the server treats ~3 missed beats as offline.</summary>
    public static readonly TimeSpan BeatInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan RedialDelay = TimeSpan.FromSeconds(5);

    /// <summary>Server-requested command deadlines are clamped into this
    /// range before they become local HTTP timeouts.</summary>
    private static readonly TimeSpan MinCommandDeadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxCommandDeadline = TimeSpan.FromMinutes(5);

    private bool _wasHealthy = true; // log the first failure, then stay quiet

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var grpcUrl = ServerGrpcUrl();
        var localControl = new Uri(
            config["Fleet:LocalControlUrl"] ?? options.ControlUrl.ToString(),
            UriKind.Absolute);
        logger.LogInformation(
            "fleet member '{MemberId}' dialing control stream at {Server}, "
            + "commands execute against {Local}",
            options.MemberId, grpcUrl, localControl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(grpcUrl, localControl, stoppingToken);
                // The server ended the stream cleanly (restart/shutdown);
                // redial after the usual pause.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_wasHealthy)
                    logger.LogWarning(
                        "fleet control stream failed: {Error} (redialing quietly)", ex.Message);
                _wasHealthy = false;
            }
            try
            {
                await Task.Delay(RedialDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One attached session: hello, then the beat loop and the
    /// server-frame reader run until the stream ends or fails. Tunnel
    /// dial-backs run as children of the session token.</summary>
    private async Task RunSessionAsync(Uri grpcUrl, Uri localControl, CancellationToken ct)
    {
        using var channel = GrpcChannel.ForAddress(grpcUrl);
        var client = new FleetControlRpc.FleetControlClient(channel);
        using var call = client.Attach(cancellationToken: ct);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // gRPC allows one writer at a time; hello/beats/replies interleave.
        var writeLock = new SemaphoreSlim(1, 1);
        async Task WriteAsync(MemberFrame frame)
        {
            await writeLock.WaitAsync(session.Token);
            try
            {
                await call.RequestStream.WriteAsync(frame);
            }
            finally
            {
                writeLock.Release();
            }
        }

        var hello = new MemberHello
        {
            MemberId = options.MemberId,
            Version = typeof(FleetControlDialerService).Assembly
                .GetName().Version?.ToString() ?? "dev",
            ControlUrl = options.ControlUrl.ToString(),
            Os = HostSpecs.Os,
            Cpu = HostSpecs.Cpu ?? "",
            CpuCores = (uint)Math.Max(HostSpecs.CpuCores, 0),
            RamMb = (ulong)Math.Max(HostSpecs.RamMb, 0),
            Gpu = HostSpecs.Gpu ?? "",
        };
        hello.Capabilities.AddRange(options.Capabilities);
        await WriteAsync(new MemberFrame { Hello = hello });

        if (!_wasHealthy)
            logger.LogInformation("fleet control stream recovered");
        else
            logger.LogDebug("fleet control stream attached");
        _wasHealthy = true;

        // In-flight beats by seq (sent timestamp); the ack round-trip is the
        // next beat's self-reported RTT.
        var sentBeats = new ConcurrentDictionary<ulong, long>();
        var rttMs = 0d;

        var reader = Task.Run(async () =>
        {
            await foreach (var frame in call.ResponseStream.ReadAllAsync(session.Token))
            {
                switch (frame.MsgCase)
                {
                    case ServerFrame.MsgOneofCase.BeatAck:
                        if (sentBeats.TryRemove(frame.BeatAck.Seq, out var sentAt))
                            rttMs = Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds;
                        break;
                    case ServerFrame.MsgOneofCase.Command:
                        var command = frame.Command;
                        _ = Task.Run(
                            () => ExecuteCommandAsync(
                                command, localControl, WriteAsync, session.Token),
                            session.Token);
                        break;
                    case ServerFrame.MsgOneofCase.TunnelOpen:
                        var open = frame.TunnelOpen;
                        _ = Task.Run(
                            () => RunTunnelAsync(grpcUrl, open, session.Token),
                            session.Token);
                        break;
                }
            }
        }, session.Token);

        try
        {
            ulong seq = 0;
            while (!session.Token.IsCancellationRequested && !reader.IsCompleted)
            {
                seq++;
                sentBeats[seq] = Stopwatch.GetTimestamp();
                await WriteAsync(new MemberFrame
                {
                    Beat = new MemberBeat
                    {
                        Seq = seq,
                        Load = NormalizedCpuLoad(),
                        RttMs = rttMs,
                    },
                });
                await Task.Delay(BeatInterval, session.Token);
            }
            await reader; // surface the server-side close or transport error
        }
        finally
        {
            session.Cancel();
        }
    }

    /// <summary>Executes one HTTP-shaped command against the local control
    /// listener and streams the outcome back; local failures become a
    /// status-0 reply (never an unanswered correlation id).</summary>
    private async Task ExecuteCommandAsync(
        Command command, Uri localControl,
        Func<MemberFrame, Task> write, CancellationToken ct)
    {
        var reply = new CommandReply { CorrelationId = command.CorrelationId };
        try
        {
            var client = httpFactory.CreateClient(nameof(FleetControlDialerService));
            client.BaseAddress = localControl;
            client.Timeout = TimeSpan.FromMilliseconds(Math.Clamp(
                command.DeadlineMs,
                MinCommandDeadline.TotalMilliseconds, MaxCommandDeadline.TotalMilliseconds));
            var method = string.Equals(command.Method, "GET", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Get : HttpMethod.Post;
            using var request = new HttpRequestMessage(method, command.Path);
            if (method == HttpMethod.Post)
                request.Content = new StringContent(
                    command.BodyJson is { Length: > 0 } body ? body : "{}",
                    Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, ct);
            reply.Status = (uint)response.StatusCode;
            reply.BodyJson = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            reply.Status = 0;
            reply.Error = ex.Message;
        }
        if (ct.IsCancellationRequested)
            return; // session is gone; the server's call times out on its own
        try
        {
            await write(new MemberFrame { Reply = reply });
        }
        catch
        {
            // Stream died between execution and reply — same as above.
        }
    }

    /// <summary>Dials back one Tunnel stream for a server TunnelOpen, binds
    /// the id, then hands the duplex to the handler registered for the
    /// purpose. Handler RpcExceptions ride back as the tunnel's terminal
    /// status; anything else closes it as INTERNAL.</summary>
    private async Task RunTunnelAsync(Uri grpcUrl, TunnelOpen open, CancellationToken ct)
    {
        var handler = tunnelHandlers.FirstOrDefault(h =>
            string.Equals(h.Purpose, open.Purpose, StringComparison.Ordinal));
        using var channel = GrpcChannel.ForAddress(grpcUrl);
        var client = new FleetControlRpc.FleetControlClient(channel);
        using var call = client.Tunnel(cancellationToken: ct);
        try
        {
            // First frame binds the dial-back to its TunnelOpen.
            await call.RequestStream.WriteAsync(new TunnelFrame { TunnelId = open.TunnelId });

            var closing = new TunnelFrame { Closed = true };
            if (handler is null)
            {
                logger.LogWarning(
                    "tunnel open for unknown purpose '{Purpose}' (target={Target})",
                    open.Purpose, open.Target);
                closing.GrpcStatus = (uint)StatusCode.Unimplemented;
                closing.Error = $"no handler for tunnel purpose '{open.Purpose}'";
            }
            else
            {
                try
                {
                    await handler.RunAsync(open.Target, new TunnelDuplex(call), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return; // session is gone; the server side unwinds itself
                }
                catch (RpcException ex)
                {
                    closing.GrpcStatus = (uint)ex.StatusCode;
                    closing.Error = ex.Status.Detail;
                }
                catch (Exception ex)
                {
                    closing.GrpcStatus = (uint)StatusCode.Internal;
                    closing.Error = ex.Message;
                }
            }
            await call.RequestStream.WriteAsync(closing);
            await call.RequestStream.CompleteAsync();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "tunnel {TunnelId} ({Purpose}) failed: {Error}",
                open.TunnelId, open.Purpose, ex.Message);
        }
    }

    /// <summary>Best-effort load hint for the beats: this process's recent
    /// CPU time over wall time across all cores, clamped to 0..1.</summary>
    internal static double NormalizedCpuLoad()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime.TotalMilliseconds;
            var wall = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
            if (wall <= 0) return 0;
            return Math.Clamp(cpu / wall / Environment.ProcessorCount, 0, 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Adapts one Tunnel call's streams to the handler-facing
    /// byte duplex.</summary>
    private sealed class TunnelDuplex(
        AsyncDuplexStreamingCall<TunnelFrame, TunnelFrame> call) : IFleetTunnelDuplex
    {
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var frame in call.ResponseStream.ReadAllAsync(ct))
            {
                if (frame.Closed)
                    yield break;
                yield return frame.Payload.Memory;
            }
        }

        public Task WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken ct) =>
            call.RequestStream.WriteAsync(
                new TunnelFrame { Payload = Google.Protobuf.ByteString.CopyFrom(payload.Span) },
                ct);
    }

    /// <summary>Server:GrpcUrl, or the Server:BaseUrl host on the server's
    /// default gRPC port (h2c).</summary>
    private Uri ServerGrpcUrl()
    {
        if (config["Server:GrpcUrl"] is { Length: > 0 } url)
            return new Uri(url, UriKind.Absolute);
        return new UriBuilder(options.ServerBaseUrl)
        {
            Scheme = "http", // the server's gRPC listener is cleartext HTTP/2
            Port = 50052,
            Path = "",
            Query = "",
        }.Uri;
    }
}

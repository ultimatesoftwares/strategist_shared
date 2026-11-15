namespace Strategist.FleetMember;

/// <summary>
/// Member-side bridge for one FleetControl tunnel purpose (e.g. "screen").
/// When the server sends TunnelOpen down the control stream, the dialer
/// dials back a Tunnel RPC and hands this handler the duplex: the handler
/// bridges tunnel payload bytes to whatever local stream the purpose means.
/// Throwing an RpcException ends the tunnel with that status (the server
/// relays it to the original caller); returning normally ends it with OK.
/// </summary>
public interface IFleetTunnelHandler
{
    /// <summary>The TunnelOpen.purpose this handler serves.</summary>
    string Purpose { get; }

    /// <summary>Bridges one tunnel until either side ends it.</summary>
    /// <param name="target">The purpose-defined TunnelOpen.target
    /// (screen: the terminal id).</param>
    Task RunAsync(string target, IFleetTunnelDuplex tunnel, CancellationToken ct);
}

/// <summary>One open tunnel's payload duplex, direction-agnostic bytes.</summary>
public interface IFleetTunnelDuplex
{
    /// <summary>Server→member payloads until the server closes the tunnel.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(CancellationToken ct);

    /// <summary>Sends one member→server payload.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);
}

namespace LSE.NET;

/// <summary>Published endpoints for the four standard LSE live price feeds.</summary>
public static class LseStreamEndpoints
{
    /// <summary>US securities, forex, commodities, futures and US bond yields.</summary>
    public static Uri Data { get; } = new("wss://data-ws.londonstrategicedge.com");
    /// <summary>Futures and major forex pairs.</summary>
    public static Uri Chicago { get; } = new("wss://chicago-ws.londonstrategicedge.com");
    /// <summary>UK stocks and crypto.</summary>
    public static Uri London { get; } = new("wss://london-ws.londonstrategicedge.com");
    /// <summary>Canadian stocks.</summary>
    public static Uri Toronto { get; } = new("wss://tsx-ws.londonstrategicedge.com");
}

/// <summary>Settings for an authenticated live feed connection.</summary>
public sealed class LseStreamOptions
{
    /// <summary>API key sent in the auth frame, never in the URL or headers.</summary>
    public string ApiKey { get; init; } = "";
    /// <summary>A trusted WebSocket endpoint using the standard LSE protocol.</summary>
    public Uri Endpoint { get; init; } = LseStreamEndpoints.Data;
    /// <summary>Total time allowed to connect, receive welcome, and authenticate.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Interval between application ping messages.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(25);
    /// <summary>Maximum UTF-8 bytes in a single inbound message, including catalogues.</summary>
    public int MaxMessageBytes { get; init; } = 32 * 1024 * 1024;
}

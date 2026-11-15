using Microsoft.Extensions.Configuration;

namespace Strategist.FleetMember;

/// <summary>Member identity + where to find the master server's REST API
/// (appsettings.json / environment: Server__BaseUrl, Fleet__Id, …) and the
/// control URL this member advertises (Fleet__ControlUrl — the address the
/// SERVER dials for engine control / provisioning / job queries).</summary>
public sealed record FleetMemberOptions(
    Uri ServerBaseUrl,
    string MemberId,
    IReadOnlyList<string> Capabilities,
    Uri ControlUrl)
{
    public static FleetMemberOptions From(
        IConfiguration config, string defaultControlUrl, params string[] defaultCapabilities)
    {
        var url = config["Server:BaseUrl"] ?? "http://127.0.0.1:8000";
        var id = config["Fleet:Id"];
        var caps = config.GetSection("Fleet:Capabilities").Get<string[]>();
        var effectiveCaps = caps is { Length: > 0 } ? caps : defaultCapabilities;
        // Default id is role-qualified: several members (engine, backtest)
        // often share one machine, and the server's directory is keyed by
        // id — a bare hostname would make them replace each other.
        var defaultId = effectiveCaps.Length > 0
            ? $"{Environment.MachineName}-{effectiveCaps[0]}"
            : Environment.MachineName;
        var control = config["Fleet:ControlUrl"] ?? defaultControlUrl;
        return new FleetMemberOptions(
            new Uri(url),
            string.IsNullOrWhiteSpace(id) ? defaultId : id,
            effectiveCaps,
            new Uri(control));
    }
}

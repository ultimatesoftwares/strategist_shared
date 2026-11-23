using System.Globalization;
using LSE.NET;

var demo = args.Contains("--demo");
var once = args.Contains("--once");
if (args.Contains("--help") || args.Any(arg => arg is not ("--demo" or "--once")))
{
    Console.WriteLine("Usage: dotnet run --project examples/Tui -- [--demo] [--once]");
    return args.Contains("--help") ? 0 : 2;
}

var key = demo ? "demo-not-a-real-key" : Environment.GetEnvironmentVariable("LSE_API_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Set LSE_API_KEY in your environment, or run with --demo.");
    return 2;
}
if (!once && (Console.IsInputRedirected || Console.IsOutputRedirected))
{
    Console.Error.WriteLine("The dashboard needs an interactive terminal. Use --once for scripts.");
    return 2;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    using var rest = new LseDataClient(new LseOptions { ApiKey = key, TickFormat = demo ? "csv" : "parquet" },
        demo ? new DemoRestHandler() : null);
    await using var dashboard = new Dashboard(rest, key, demo, stop.Token);
    if (once)
        await dashboard.RunOnceAsync();
    else
        await dashboard.RunAsync();
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    return 130;
}
catch (Exception e) when (e is LseException or HttpRequestException or System.Net.WebSockets.WebSocketException
    or ArgumentException or FormatException)
{
    Console.Error.WriteLine(Dashboard.Error(e));
    return 1;
}

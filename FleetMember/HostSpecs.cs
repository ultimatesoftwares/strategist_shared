using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Strategist.FleetMember;

/// <summary>
/// Best-effort description of the hardware this member runs on, probed once
/// per process and self-reported in the <see cref="FleetControlDialerService"/>
/// hello so the fleet pages can show each member's iron (CPU, GPU, RAM, OS).
/// Every probe tolerates failure — a field is simply null when the host
/// does not expose it. Advisory only: nothing schedules off these values.
/// </summary>
public static class HostSpecs
{
    /// <summary>OS description + process architecture, e.g. "Linux 6.8 (X64)".</summary>
    public static readonly string Os =
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>Logical processors visible to this process.</summary>
    public static readonly int CpuCores = Environment.ProcessorCount;

    /// <summary>Total physical memory (or the container's limit), MB.</summary>
    public static readonly long RamMb = ProbeRamMb();

    /// <summary>CPU model name; null when the host doesn't expose one.</summary>
    public static readonly string? Cpu = ProbeCpuModel();

    /// <summary>Display adapter name(s), comma-separated; null when none
    /// were detected (headless hosts, containers without /sys access).</summary>
    public static readonly string? Gpu = ProbeGpuModels();

    private static long ProbeRamMb()
    {
        try
        {
            // Physical RAM, or the cgroup/job limit when containerized —
            // which is the honest answer for "what this member can use".
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    private static string? ProbeCpuModel()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                        return Collapse(line[(line.IndexOf(':') + 1)..]);
                return null;
            }
            if (OperatingSystem.IsWindows())
                return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (OperatingSystem.IsMacOS())
                return RunForOutput("sysctl", "-n machdep.cpu.brand_string")?.Trim();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ProbeGpuModels()
    {
        if (!OperatingSystem.IsLinux())
            return null; // keep the probe surface small; null is fine elsewhere
        try
        {
            // lspci -mm quotes each field after the slot:
            //   00:02.0 "VGA compatible controller" "Intel Corporation" "HD Graphics 620" ...
            var output = RunForOutput("lspci", "-mm");
            if (output is null) return null;
            var names = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                var fields = Regex.Matches(line, "\"([^\"]*)\"");
                if (fields.Count < 3) continue;
                var klass = fields[0].Groups[1].Value;
                if (!klass.Contains("VGA", StringComparison.OrdinalIgnoreCase)
                    && !klass.Contains("3D", StringComparison.OrdinalIgnoreCase)
                    && !klass.Contains("Display", StringComparison.OrdinalIgnoreCase))
                    continue;
                names.Add(Collapse($"{fields[1].Groups[1].Value} {fields[2].Groups[1].Value}"));
            }
            return names.Count > 0 ? string.Join(", ", names) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs a short-lived probe command, returning stdout — or null
    /// when the binary is missing, fails, or takes longer than 2 s.</summary>
    private static string? RunForOutput(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (process is null) return null;
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(2000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return null;
        }
        return process.ExitCode == 0 ? output : null;
    }

    private static string Collapse(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");
}

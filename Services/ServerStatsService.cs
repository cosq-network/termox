using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Gathers server health statistics (CPU, processes, memory, disk) over an SSH connection.
/// Uses standard POSIX commands (ps, df, /proc/meminfo) so it works on most Linux servers
/// and reports a clear message when a command is unavailable or the output is unexpected.
/// </summary>
public class ServerStatsService
{
    public class ProcessInfo
    {
        public string Pid { get; set; } = "";
        public string User { get; set; } = "";
        public double CpuPercent { get; set; }
        public double MemPercent { get; set; }
        public long RssBytes { get; set; }
        public string Command { get; set; } = "";

        public string CpuPercentDisplay => $"{CpuPercent:F1}%";
        public string MemPercentDisplay => $"{MemPercent:F1}%";
        public string RssDisplay => FormatBytes(RssBytes);
    }

    public class DiskVolumeInfo
    {
        public string Filesystem { get; set; } = "";
        public long SizeBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsePercent { get; set; }
        public string MountedOn { get; set; } = "";

        public string SizeDisplay => FormatBytes(SizeBytes);
        public string UsedDisplay => FormatBytes(UsedBytes);
        public string AvailableDisplay => FormatBytes(AvailableBytes);
        public string UsePercentDisplay => $"{UsePercent:F0}%";
    }

    public class MemoryInfo
    {
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsedPercent { get; set; }

        public string TotalDisplay => FormatBytes(TotalBytes);
        public string UsedDisplay => FormatBytes(UsedBytes);
        public string AvailableDisplay => FormatBytes(AvailableBytes);
        public string UsedPercentDisplay => UsedPercent < 0 ? "—" : $"{UsedPercent:F1}%";

        public static MemoryInfo Unknown => new() { UsedPercent = -1 };
    }

    public class ServerStats
    {
        public string Host { get; set; } = "";
        public string OsInfo { get; set; } = "";
        public string Kernel { get; set; } = "";
        public string Uptime { get; set; } = "";
        public string LoadAverage { get; set; } = "";
        public double CpuUsagePercent { get; set; } = -1;
        public MemoryInfo Memory { get; set; } = MemoryInfo.Unknown;
        public List<ProcessInfo> TopCpuProcesses { get; } = new();
        public List<ProcessInfo> TopMemoryProcesses { get; } = new();
        public List<DiskVolumeInfo> Disks { get; } = new();
    }

    private const string PsColumns = "pid,user,pcpu,pmem,rss,comm,args";

    /// <summary>
    /// Formats a byte count using binary units (KiB/MiB/GiB) with one decimal place,
    /// e.g. "1.5 GB". Shared by every display property so sizes are consistent.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        int place = Math.Min((int)Math.Floor(Math.Log(bytes, 1024)), suf.Length - 1);
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{num} {suf[place]}";
    }

    /// <summary>
    /// Establishes a temporary SSH session using the same auth path as the rest of the app
    /// (SshSecurity host-key policy + SSH.NET), runs the stats commands, and closes it.
    /// </summary>
    public async Task<ServerStats> CollectAsync(
        SshConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        var stats = new ServerStats { Host = profile.Host };

        if (string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.Username))
            return stats;

        SshClient client;
        var safeUsername = profile.Username ?? "";
        var safePassword = profile.Password ?? "";

        SshSecurity.EnsurePrivateKeyExists(profile.PrivateKeyPath);

        if (!string.IsNullOrWhiteSpace(profile.PrivateKeyPath) && File.Exists(profile.PrivateKeyPath))
        {
            var keyFile = new PrivateKeyFile(profile.PrivateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
            client = new SshClient(profile.Host, profile.Port, safeUsername, new[] { keyFile });
        }
        else
        {
            client = new SshClient(profile.Host, profile.Port, safeUsername, safePassword);
        }

        client.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;
        SshSecurity.ConfigureHostKeyPolicy(client, profile.HostKeyFingerprint, null);

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        try
        {
            using var command = client.CreateCommand(
                "uname -srm; cat /etc/os-release 2>/dev/null | grep '^PRETTY_NAME=' | cut -d'=' -f2; " +
                "uptime; " +
                "cat /proc/loadavg; " +
                "top -bn1 2>/dev/null | grep 'Cpu(s)' || vmstat 1 2 | tail -1; " +
                "cat /proc/meminfo; " +
                $"ps -eo {PsColumns} --sort=-pcpu 2>/dev/null | head -n 21; " +
                $"ps -eo {PsColumns} --sort=-rss 2>/dev/null | head -n 21; " +
                "df -Pk 2>/dev/null || df -k 2>/dev/null");
            command.CommandTimeout = TimeSpan.FromSeconds(15);
            await command.ExecuteAsync(cancellationToken);
            var output = command.Result ?? "";

            ParseStats(stats, output);
            return stats;
        }
        catch
        {
            // Connection-level failure — rethrow so the caller can show the error.
            throw;
        }
        finally
        {
            client.Disconnect();
            client.Dispose();
        }
    }

    internal static void ParseStats(ServerStats stats, string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return;

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        var index = 0;

        // 1) uname -srm
        if (index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]))
        {
            stats.Kernel = lines[index].Trim();
            index++;
        }

        // 2) /etc/os-release PRETTY_NAME (may span quoting)
        if (index < lines.Count)
        {
            var prettyName = lines[index].Trim();
            if (prettyName.Length > 2 && prettyName.StartsWith("\"", StringComparison.Ordinal) &&
                prettyName.EndsWith("\"", StringComparison.Ordinal))
            {
                prettyName = prettyName[1..^1];
            }
            stats.OsInfo = prettyName;
            index++;
        }

        // 3) uptime — " 14:32:11 up 3 days,  2:31,  3 users,  load average: 0.08, 0.03, 0.01"
        if (index < lines.Count)
        {
            var uptimeLine = lines[index].Trim();
            if (uptimeLine.Contains("load average", StringComparison.Ordinal))
            {
                var loadIdx = uptimeLine.IndexOf("load average:", StringComparison.Ordinal);
                stats.Uptime = uptimeLine[..loadIdx].Trim();
                stats.LoadAverage = uptimeLine[(loadIdx + "load average:".Length)..].Trim();
            }
            index++;
        }

        // 4) /proc/loadavg — "0.08 0.03 0.01 1/234 5678"
        if (index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]))
        {
            if (string.IsNullOrEmpty(stats.LoadAverage))
            {
                var parts = lines[index].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3) stats.LoadAverage = string.Join(" ", parts.Take(3));
            }
            index++;
        }

        // 5) CPU usage line(s) — from `top -bn1` or `vmstat`
        if (index < lines.Count)
        {
            ParseCpuLine(stats, lines[index]);
            index++;
        }

        // 6) /proc/meminfo — consume every meminfo line until the ps section starts.
        var memoryLines = new List<string>();
        while (index < lines.Count && IsMemInfoLine(lines[index]))
        {
            memoryLines.Add(lines[index]);
            index++;
        }
        if (memoryLines.Count > 0)
        {
            ParseMemory(memoryLines, stats);
        }

        // 7) ps -eo ... --sort=-pcpu (header + up to 20 rows)
        index = SkipPsHeader(lines, index);
        var cpuProcLines = ReadPsRows(lines, ref index);
        ParseProcesses(cpuProcLines, stats.TopCpuProcesses);

        // 8) ps -eo ... --sort=-rss (header + up to 20 rows)
        index = SkipPsHeader(lines, index);
        var memProcLines = ReadPsRows(lines, ref index);
        ParseProcesses(memProcLines, stats.TopMemoryProcesses);

        // 9) df output
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;
        if (index < lines.Count)
        {
            // First line may be the header "Filesystem  1024-blocks ..."
            if (lines[index].TrimStart().StartsWith("Filesystem", StringComparison.Ordinal))
                index++;
            for (; index < lines.Count; index++)
            {
                var disk = ParseDiskLine(lines[index]);
                if (disk != null) stats.Disks.Add(disk);
            }
        }
    }

    private static bool IsMemInfoLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("MemTotal", StringComparison.Ordinal)
            || trimmed.StartsWith("MemFree", StringComparison.Ordinal)
            || trimmed.StartsWith("MemAvailable", StringComparison.Ordinal)
            || trimmed.StartsWith("Buffers", StringComparison.Ordinal)
            || trimmed.StartsWith("Cached", StringComparison.Ordinal)
            || trimmed.StartsWith("SReclaimable", StringComparison.Ordinal);
    }

    private static int SkipPsHeader(List<string> lines, int index)
    {
        if (index < lines.Count && lines[index].Contains("PID", StringComparison.OrdinalIgnoreCase))
            index++;
        return index;
    }

    /// <summary>
    /// Reads up to 20 process rows, stopping early at the next section's header,
    /// the df header, or a blank line (short ps output).
    /// </summary>
    private static List<string> ReadPsRows(List<string> lines, ref int index)
    {
        var rows = new List<string>();
        while (rows.Count < 20 && index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("PID ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Filesystem", StringComparison.Ordinal))
                break;
            rows.Add(line);
            index++;
        }
        return rows;
    }

    private static void ParseCpuLine(ServerStats stats, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // top -bn1: "%Cpu(s):  3.2 us,  1.1 sy,  0.0 ni, 95.6 id,  0.0 wa, ..."
        var topMatch = line.IndexOf("%Cpu", StringComparison.Ordinal);
        if (topMatch >= 0)
        {
            var rest = line[(topMatch + 5)..];
            // find the "id," value
            var idIdx = rest.IndexOf(" id,", StringComparison.Ordinal);
            if (idIdx < 0) idIdx = rest.IndexOf(" id ", StringComparison.Ordinal);
            if (idIdx >= 0)
            {
                var before = rest[..idIdx];
                var lastToken = before.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault() ?? "";
                if (double.TryParse(lastToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var idle))
                {
                    stats.CpuUsagePercent = Math.Clamp(100.0 - idle, 0, 100);
                    return;
                }
            }
        }

        // vmstat: "r  b   swpd   free   buff  cache   si   so    bi    bo   in   cs us sy id wa st"
        // data row: " 1  0      0 123456 ...  3  2 95  1  0"
        var vm = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (vm.Length >= 16 && double.TryParse(vm[^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vmIdle))
        {
            // last 5 columns are us sy id wa st; id is third from the end.
            stats.CpuUsagePercent = Math.Clamp(100.0 - vmIdle, 0, 100);
        }
    }

    private static void ParseMemory(List<string> memInfoLines, ServerStats stats)
    {
        long MemTotal = 0, MemAvailable = 0, MemFree = 0, Buffers = 0, Cached = 0, SReclaimable = 0;

        foreach (var line in memInfoLines)
        {
            var parts = line.Split(':', 2, StringSplitOptions.None);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            if (!long.TryParse(parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            switch (key)
            {
                case "MemTotal": MemTotal = value; break;
                case "MemFree": MemFree = value; break;
                case "MemAvailable": MemAvailable = value; break;
                case "Buffers": Buffers = value; break;
                case "Cached": Cached = value; break;
                case "SReclaimable": SReclaimable = value; break;
            }
        }

        if (MemTotal <= 0)
        {
            stats.Memory = MemoryInfo.Unknown;
            return;
        }

        // Values are in kB from /proc/meminfo.
        var availableKb = MemAvailable > 0
            ? MemAvailable
            : MemFree + Buffers + Cached + SReclaimable;
        var usedKb = Math.Max(0, MemTotal - availableKb);

        stats.Memory = new MemoryInfo
        {
            TotalBytes = MemTotal * 1024,
            UsedBytes = usedKb * 1024,
            AvailableBytes = availableKb * 1024,
            UsedPercent = MemTotal > 0 ? Math.Round(usedKb * 100.0 / MemTotal, 1) : 0
        };
    }

    private static void ParseProcesses(List<string> lines, List<ProcessInfo> target)
    {
        // Skip a header line if present ("PID USER ...").
        var start = 0;
        if (lines.Count > 0 && lines[0].Contains("PID", StringComparison.OrdinalIgnoreCase))
            start = 1;

        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            target.Add(ParseProcessLine(line));
        }
    }

    private static ProcessInfo ParseProcessLine(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 6)
            return new ProcessInfo { Command = line };

        var rssKb = TryParseLong(tokens[4]);

        return new ProcessInfo
        {
            Pid = tokens[0],
            User = tokens[1],
            CpuPercent = TryParseDouble(tokens[2]),
            MemPercent = TryParseDouble(tokens[3]),
            RssBytes = rssKb * 1024,
            Command = string.Join(' ', tokens.Skip(5))
        };
    }

    private static DiskVolumeInfo? ParseDiskLine(string line)
    {
        var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 6) return null;

        // df -Pk reports sizes in 1024-byte blocks.
        var sizeKb = TryParseLong(tokens[1]);
        var usedKb = TryParseLong(tokens[2]);
        var availableKb = TryParseLong(tokens[3]);
        var usePercent = tokens[4].TrimEnd('%');

        return new DiskVolumeInfo
        {
            Filesystem = tokens[0],
            SizeBytes = sizeKb * 1024,
            UsedBytes = usedKb * 1024,
            AvailableBytes = availableKb * 1024,
            UsePercent = sizeKb > 0 ? Math.Round(usedKb * 100.0 / sizeKb, 1) : TryParseDouble(usePercent),
            MountedOn = string.Join(' ', tokens.Skip(5))
        };
    }

    private static double TryParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static long TryParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
}

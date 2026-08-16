using System;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ServerStatsParsingTests
{
    [Fact]
    public void ParsesCpuFromTopOutput()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats,
            "Linux host 6.8.0\n\"Ubuntu 24.04 LTS\"\n 14:32:11 up 3 days,  2:31,  3 users,  load average: 0.08, 0.03, 0.01\n0.08 0.03 0.01 1/234 5678\n%Cpu(s):  3.2 us,  1.1 sy,  0.0 ni, 95.6 id,  0.0 wa, 0.0 hi, 0.0 si, 0.0 st\n");

        Assert.Equal("Linux host 6.8.0", stats.Kernel);
        Assert.Equal("Ubuntu 24.04 LTS", stats.OsInfo);
        Assert.Contains("up 3 days", stats.Uptime);
        Assert.Equal("0.08, 0.03, 0.01", stats.LoadAverage);
        Assert.Equal(4.4, stats.CpuUsagePercent, 1);
    }

    [Fact]
    public void ParsesCpuFromVmstatFallback()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats,
            "Linux host 5.15.0\n\"Debian GNU/Linux 12\"\n 09:00:00 up 1 day,  1 user,  load average: 0.10, 0.05, 0.02\n0.10 0.05 0.02 1/100 2000\n 1  0  12345  67890  1234  56789  0  0  10  20  100  200  3  2  94  1  0\n");

        Assert.Equal(6.0, stats.CpuUsagePercent, 1);  // 100 - 94 idle
    }

    [Fact]
    public void ParsesMemoryFromProcMeminfo()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats,
            "Linux host 6.8.0\n\"Ubuntu 24.04 LTS\"\n 14:32:11 up 3 days, load average: 0.08, 0.03, 0.01\n0.08 0.03 0.01\n%Cpu(s):  5.0 us,  0.0 sy, 95.0 id\n" +
            "MemTotal:       16384000 kB\nMemFree:         4000000 kB\nMemAvailable:    6000000 kB\nBuffers:          100000 kB\nCached:          2000000 kB\n" +
            "PID USER %CPU %MEM RSS COMMAND\n");

        Assert.Equal(16_384_000L * 1024, stats.Memory.TotalBytes);
        Assert.Equal(6_000_000L * 1024, stats.Memory.AvailableBytes);
        Assert.Equal((16_384_000L - 6_000_000L) * 1024, stats.Memory.UsedBytes);
        Assert.Equal(63.4, stats.Memory.UsedPercent, 1);
    }

    [Fact]
    public void ParsesTopCpuProcesses()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats,
            "Linux host 6.8.0\n\"Ubuntu 24.04 LTS\"\n 14:32:11 up 3 days, load average: 0.08, 0.03, 0.01\n0.08 0.03 0.01\n%Cpu(s):  5.0 us,  0.0 sy, 95.0 id\n" +
            "MemTotal:       16384000 kB\nMemAvailable:    6000000 kB\n" +
            "PID USER %CPU %MEM RSS COMMAND\n" +
            "1234 root 45.6 2.0 1048576 nginx: worker process\n" +
            "5678 www 12.3 1.0 524288 php-fpm: pool www\n" +
            "9101 alice 5.0 0.5 262144 /usr/bin/python3 app.py --serve\n" +
            "PID USER %CPU %MEM RSS COMMAND\n" +
            "1234 root 45.6 2.0 1048576 nginx: worker process\n" +
            "5678 www 12.3 1.0 524288 php-fpm: pool www\n" +
            "Filesystem 1024-blocks Used Available Capacity Mounted on\n" +
            "/dev/sda1 10485760 5242880 5242880 50% /\n");

        Assert.Equal(3, stats.TopCpuProcesses.Count);
        Assert.Equal("1234", stats.TopCpuProcesses[0].Pid);
        Assert.Equal(45.6, stats.TopCpuProcesses[0].CpuPercent);
        Assert.Equal(2.0, stats.TopCpuProcesses[0].MemPercent);
        Assert.Equal(1048576L * 1024, stats.TopCpuProcesses[0].RssBytes);
        Assert.Equal("nginx: worker process", stats.TopCpuProcesses[0].Command);
        Assert.Equal("1 GB", stats.TopCpuProcesses[0].RssDisplay);
        Assert.Equal(2, stats.TopMemoryProcesses.Count);
        Assert.Equal("/usr/bin/python3 app.py --serve", stats.TopCpuProcesses[2].Command);
    }

    [Fact]
    public void ParsesDiskVolumes()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats,
            "Linux host 6.8.0\n\"Ubuntu 24.04 LTS\"\n 14:32:11 up 3 days, load average: 0.08, 0.03, 0.01\n0.08 0.03 0.01\n%Cpu(s):  5.0 us,  0.0 sy, 95.0 id\n" +
            "MemTotal:       16384000 kB\nMemAvailable:    6000000 kB\n" +
            "PID USER %CPU %MEM RSS COMMAND\n1234 root 1.0 0.1 1024 bash\n" +
            "PID USER %CPU %MEM RSS COMMAND\n5678 user 2.0 0.2 2048 zsh\n" +
            "Filesystem 1024-blocks Used Available Capacity Mounted on\n" +
            "/dev/sda1 10485760 5242880 5242880 50% /\n" +
            "tmpfs 1024000 0 1024000 0% /dev/shm\n");

        Assert.Equal(2, stats.Disks.Count);
        Assert.Equal("/dev/sda1", stats.Disks[0].Filesystem);
        Assert.Equal(10485760L * 1024, stats.Disks[0].SizeBytes);
        Assert.Equal(5242880L * 1024, stats.Disks[0].AvailableBytes);
        Assert.Equal(50.0, stats.Disks[0].UsePercent);
        Assert.Equal("50%", stats.Disks[0].UsePercentDisplay);
        Assert.Equal("10 GB", stats.Disks[0].SizeDisplay);
        Assert.Equal("/", stats.Disks[0].MountedOn);
        Assert.Equal("tmpfs", stats.Disks[1].Filesystem);
    }

    [Fact]
    public void FormatsBytesInHumanReadableUnits()
    {
        Assert.Equal("0 B", ServerStatsService.FormatBytes(0));
        Assert.Equal("500 B", ServerStatsService.FormatBytes(500));
        Assert.Equal("1 KB", ServerStatsService.FormatBytes(1024));
        Assert.Equal("1.5 KB", ServerStatsService.FormatBytes(1536));
        Assert.Equal("1 MB", ServerStatsService.FormatBytes(1024 * 1024));
        Assert.Equal("1 GB", ServerStatsService.FormatBytes(1024L * 1024 * 1024));
        Assert.Equal("1.5 GB", ServerStatsService.FormatBytes((long)(1.5 * 1024 * 1024 * 1024)));
        Assert.Equal("1 TB", ServerStatsService.FormatBytes(1024L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void EmptyOutputLeavesUnknownState()
    {
        var stats = new ServerStatsService.ServerStats();
        ServerStatsService.ParseStats(stats, "");

        Assert.Equal("", stats.Kernel);
        Assert.Equal(-1, stats.CpuUsagePercent);
        Assert.Empty(stats.TopCpuProcesses);
        Assert.Empty(stats.Disks);
    }
}

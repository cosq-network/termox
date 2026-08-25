using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

public sealed class ServerStatsTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly Action<ServerStatsTabViewModel> _onClose;
    private readonly ServerStatsService _statsService = new();
    private CancellationTokenSource? _collectCancellation;

    public string Title => "Server Stats";

    // Host selection
    public ObservableCollection<SshConnectionProfile> HostProfiles { get; } = new();

    private SshConnectionProfile? _selectedProfile;
    public SshConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
        }
    }

    private bool _isCollecting;
    public bool IsCollecting
    {
        get => _isCollecting;
        private set
        {
            if (_isCollecting == value) return;
            _isCollecting = value;
            OnPropertyChanged();
            (CollectStatsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _status = "Select a saved session to view its server statistics.";
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#8fbed0";
    public string StatusColor
    {
        get => _statusColor;
        private set { _statusColor = value; OnPropertyChanged(); }
    }

    // Overview
    private string _host = "";
    public string Host
    {
        get => _host;
        private set { _host = value; OnPropertyChanged(); }
    }

    private string _osInfo = "";
    public string OsInfo
    {
        get => _osInfo;
        private set { _osInfo = value; OnPropertyChanged(); }
    }

    private string _kernel = "";
    public string Kernel
    {
        get => _kernel;
        private set { _kernel = value; OnPropertyChanged(); }
    }

    private string _uptime = "";
    public string Uptime
    {
        get => _uptime;
        private set { _uptime = value; OnPropertyChanged(); }
    }

    private string _loadAverage = "";
    public string LoadAverage
    {
        get => _loadAverage;
        private set { _loadAverage = value; OnPropertyChanged(); }
    }

    // ---- CPU summary ----
    private double _cpuUsagePercent = -1;
    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            _cpuUsagePercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuUsageDisplay));
            OnPropertyChanged(nameof(CpuUsageColor));
            OnPropertyChanged(nameof(CpuBarWidth));
            OnPropertyChanged(nameof(CpuSummary));
        }
    }

    public string CpuUsageDisplay => CpuUsagePercent < 0 ? "—" : $"{CpuUsagePercent:F1}%";
    public string CpuUsageColor => UsageColor(CpuUsagePercent, 50, 80);
    public string CpuBarWidth => BarWidth(CpuUsagePercent);

    /// <summary>One-line summary of the CPU state, e.g. "4.4% used · load 0.08, 0.03, 0.01 · 10 top processes".</summary>
    public string CpuSummary
    {
        get
        {
            var parts = new List<string>();
            parts.Add(CpuUsagePercent < 0 ? "CPU usage unavailable" : $"CPU {CpuUsagePercent:F1}% used");
            if (!string.IsNullOrWhiteSpace(LoadAverage)) parts.Add($"load {LoadAverage}");
            parts.Add($"{TopCpuProcesses.Count} top processes");
            return string.Join(" · ", parts);
        }
    }

    // ---- Memory summary ----
    private string _memoryTotal = "—";
    public string MemoryTotal
    {
        get => _memoryTotal;
        private set { _memoryTotal = value; OnPropertyChanged(); }
    }

    private string _memoryUsed = "—";
    public string MemoryUsed
    {
        get => _memoryUsed;
        private set { _memoryUsed = value; OnPropertyChanged(); }
    }

    private string _memoryAvailable = "—";
    public string MemoryAvailable
    {
        get => _memoryAvailable;
        private set { _memoryAvailable = value; OnPropertyChanged(); }
    }

    private double _memoryUsedPercent = -1;
    public double MemoryUsedPercent
    {
        get => _memoryUsedPercent;
        private set
        {
            _memoryUsedPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemoryUsedDisplay));
            OnPropertyChanged(nameof(MemoryUsedColor));
            OnPropertyChanged(nameof(MemoryBarWidth));
            OnPropertyChanged(nameof(MemorySummary));
        }
    }

    public string MemoryUsedDisplay => MemoryUsedPercent < 0 ? "—" : $"{MemoryUsedPercent:F1}%";
    public string MemoryUsedColor => UsageColor(MemoryUsedPercent, 70, 90);
    public string MemoryBarWidth => BarWidth(MemoryUsedPercent);

    /// <summary>One-line summary of memory, e.g. "6.3 GB of 15.6 GB used (63.4%) · 8.9 GB available".</summary>
    public string MemorySummary
    {
        get
        {
            if (MemoryUsedPercent < 0) return "Memory usage unavailable";
            return $"{MemoryUsed} of {MemoryTotal} used ({MemoryUsedPercent:F1}%) · {MemoryAvailable} available";
        }
    }

    // ---- Disk summary ----
    private string _diskSummary = "";
    public string DiskSummary
    {
        get => _diskSummary;
        private set { _diskSummary = value; OnPropertyChanged(); }
    }

    private long _diskTotalBytes;
    private long _diskUsedBytes;
    private long _diskAvailableBytes;

    private double _diskUsedPercent = -1;
    public double DiskUsedPercent
    {
        get => _diskUsedPercent;
        private set
        {
            _diskUsedPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiskUsedDisplay));
            OnPropertyChanged(nameof(DiskUsedColor));
            OnPropertyChanged(nameof(DiskBarWidth));
        }
    }

    public string DiskUsedDisplay => DiskUsedPercent < 0 ? "—" : $"{DiskUsedPercent:F1}%";
    public string DiskUsedColor => UsageColor(DiskUsedPercent, 70, 90);
    public string DiskBarWidth => BarWidth(DiskUsedPercent);

    // ---- Sort state for the three tables ----
    public enum ProcessSort { Cpu, Memory, Pid, Name }

    private ProcessSort _cpuSort = ProcessSort.Cpu;
    public ProcessSort CpuSort
    {
        get => _cpuSort;
        private set
        {
            _cpuSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuSortIndicator));
            OnPropertyChanged(nameof(CpuIndicatorCpu));
            OnPropertyChanged(nameof(CpuIndicatorMemory));
            OnPropertyChanged(nameof(CpuIndicatorPid));
            OnPropertyChanged(nameof(CpuIndicatorName));
            RefreshCpuProcesses();
        }
    }

    private bool _cpuSortDescending = true;
    public bool CpuSortDescending
    {
        get => _cpuSortDescending;
        private set
        {
            _cpuSortDescending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuSortIndicator));
            OnPropertyChanged(nameof(CpuIndicatorCpu));
            OnPropertyChanged(nameof(CpuIndicatorMemory));
            OnPropertyChanged(nameof(CpuIndicatorPid));
            OnPropertyChanged(nameof(CpuIndicatorName));
            RefreshCpuProcesses();
        }
    }

    private ProcessSort _memorySort = ProcessSort.Memory;
    public ProcessSort MemorySort
    {
        get => _memorySort;
        private set
        {
            _memorySort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemorySortIndicator));
            OnPropertyChanged(nameof(MemoryIndicatorCpu));
            OnPropertyChanged(nameof(MemoryIndicatorMemory));
            OnPropertyChanged(nameof(MemoryIndicatorPid));
            OnPropertyChanged(nameof(MemoryIndicatorName));
            RefreshMemoryProcesses();
        }
    }

    private bool _memorySortDescending = true;
    public bool MemorySortDescending
    {
        get => _memorySortDescending;
        private set
        {
            _memorySortDescending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemorySortIndicator));
            OnPropertyChanged(nameof(MemoryIndicatorCpu));
            OnPropertyChanged(nameof(MemoryIndicatorMemory));
            OnPropertyChanged(nameof(MemoryIndicatorPid));
            OnPropertyChanged(nameof(MemoryIndicatorName));
            RefreshMemoryProcesses();
        }
    }

    private string _diskSort = "MountedOn";
    public string DiskSort
    {
        get => _diskSort;
        private set
        {
            _diskSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiskSortIndicator));
            OnPropertyChanged(nameof(DiskIndicatorFilesystem));
            OnPropertyChanged(nameof(DiskIndicatorSize));
            OnPropertyChanged(nameof(DiskIndicatorUsed));
            OnPropertyChanged(nameof(DiskIndicatorAvailable));
            OnPropertyChanged(nameof(DiskIndicatorUsePercent));
            OnPropertyChanged(nameof(DiskIndicatorMountedOn));
            RefreshDisks();
        }
    }

    private bool _diskSortDescending;
    public bool DiskSortDescending
    {
        get => _diskSortDescending;
        private set
        {
            _diskSortDescending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiskSortIndicator));
            OnPropertyChanged(nameof(DiskIndicatorFilesystem));
            OnPropertyChanged(nameof(DiskIndicatorSize));
            OnPropertyChanged(nameof(DiskIndicatorUsed));
            OnPropertyChanged(nameof(DiskIndicatorAvailable));
            OnPropertyChanged(nameof(DiskIndicatorUsePercent));
            OnPropertyChanged(nameof(DiskIndicatorMountedOn));
            RefreshDisks();
        }
    }

    // Sort column labels and indicators (▲ descending / ▼ ascending)
    public string CpuSortIndicator => CpuSortDescending ? "▲" : "▼";
    public string MemorySortIndicator => MemorySortDescending ? "▲" : "▼";
    public string DiskSortIndicator => DiskSortDescending ? "▲" : "▼";

    // Per-column indicator glyphs so the active column shows ▲/▼.
    public string CpuIndicatorPid => SortGlyph(CpuSort == ProcessSort.Pid, CpuSortDescending);
    public string CpuIndicatorCpu => SortGlyph(CpuSort == ProcessSort.Cpu, CpuSortDescending);
    public string CpuIndicatorMemory => SortGlyph(CpuSort == ProcessSort.Memory, CpuSortDescending);
    public string CpuIndicatorName => SortGlyph(CpuSort == ProcessSort.Name, CpuSortDescending);

    public string MemoryIndicatorPid => SortGlyph(MemorySort == ProcessSort.Pid, MemorySortDescending);
    public string MemoryIndicatorCpu => SortGlyph(MemorySort == ProcessSort.Cpu, MemorySortDescending);
    public string MemoryIndicatorMemory => SortGlyph(MemorySort == ProcessSort.Memory, MemorySortDescending);
    public string MemoryIndicatorName => SortGlyph(MemorySort == ProcessSort.Name, MemorySortDescending);

    public string DiskIndicatorFilesystem => SortGlyph(DiskSort == "Filesystem", DiskSortDescending);
    public string DiskIndicatorSize => SortGlyph(DiskSort == "Size", DiskSortDescending);
    public string DiskIndicatorUsed => SortGlyph(DiskSort == "Used", DiskSortDescending);
    public string DiskIndicatorAvailable => SortGlyph(DiskSort == "Available", DiskSortDescending);
    public string DiskIndicatorUsePercent => SortGlyph(DiskSort == "UsePercent", DiskSortDescending);
    public string DiskIndicatorMountedOn => SortGlyph(DiskSort == "MountedOn", DiskSortDescending);

    private static string SortGlyph(bool active, bool descending)
        => active ? (descending ? "▲" : "▼") : "";

    // Results (full sets from the service, sorted on the fly)
    private readonly List<ServerStatsService.ProcessInfo> _allCpuProcesses = new();
    private readonly List<ServerStatsService.ProcessInfo> _allMemoryProcesses = new();
    private readonly List<ServerStatsService.DiskVolumeInfo> _allDisks = new();

    public ObservableCollection<ServerStatsService.ProcessInfo> TopCpuProcesses { get; } = new();
    public ObservableCollection<ServerStatsService.ProcessInfo> TopMemoryProcesses { get; } = new();
    public ObservableCollection<ServerStatsService.DiskVolumeInfo> Disks { get; } = new();

    // Commands
    public ICommand CollectStatsCommand { get; }
    public ICommand SortCpuCommand { get; }
    public ICommand SortMemoryCommand { get; }
    public ICommand SortDiskCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }

    public ServerStatsTabViewModel(Action<ServerStatsTabViewModel> onClose,
        IEnumerable<SshConnectionProfile>? savedConnections = null)
    {
        _onClose = onClose;

        foreach (var profile in savedConnections ?? Enumerable.Empty<SshConnectionProfile>())
        {
            HostProfiles.Add(profile);
        }
        SelectedProfile = HostProfiles.FirstOrDefault();
        if (SelectedProfile == null)
        {
            Status = "No saved sessions available. Add a connection first.";
            StatusColor = "#f39c12";
        }

        CollectStatsCommand = new RelayCommand(CollectStats_Execute, () => !IsCollecting);
        SortCpuCommand = new RelayCommand<string>(ToggleCpuSort);
        SortMemoryCommand = new RelayCommand<string>(ToggleMemorySort);
        SortDiskCommand = new RelayCommand<string>(ToggleDiskSort);
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => _onClose(this));
    }

    // ---- Sort handling ----
    private void ToggleCpuSort(string? column)
    {
        if (!TrySelectSort(column, out var selected)) return;
        if (selected == CpuSort) CpuSortDescending = !CpuSortDescending;
        else { CpuSort = selected; CpuSortDescending = true; }
    }

    private void ToggleMemorySort(string? column)
    {
        if (!TrySelectSort(column, out var selected)) return;
        if (selected == MemorySort) MemorySortDescending = !MemorySortDescending;
        else { MemorySort = selected; MemorySortDescending = true; }
    }

    private void ToggleDiskSort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(column, DiskSort, StringComparison.Ordinal))
        {
            DiskSortDescending = !DiskSortDescending;
        }
        else
        {
            DiskSort = column;
            DiskSortDescending = false;
        }
    }

    private static bool TrySelectSort(string? column, out ProcessSort selected)
    {
        selected = ProcessSort.Cpu;
        if (string.IsNullOrWhiteSpace(column)) return false;

        selected = column.ToLowerInvariant() switch
        {
            "cpu" => ProcessSort.Cpu,
            "memory" or "mem" => ProcessSort.Memory,
            "pid" => ProcessSort.Pid,
            "name" or "command" => ProcessSort.Name,
            _ => ProcessSort.Cpu
        };
        return true;
    }

    private void RefreshCpuProcesses()
    {
        var sorted = SortProcesses(_allCpuProcesses, CpuSort, CpuSortDescending);
        Dispatcher.UIThread.Post(() =>
        {
            TopCpuProcesses.Clear();
            foreach (var process in sorted) TopCpuProcesses.Add(process);
            OnPropertyChanged(nameof(CpuSummary));
        });
    }

    private void RefreshMemoryProcesses()
    {
        var sorted = SortProcesses(_allMemoryProcesses, MemorySort, MemorySortDescending);
        Dispatcher.UIThread.Post(() =>
        {
            TopMemoryProcesses.Clear();
            foreach (var process in sorted) TopMemoryProcesses.Add(process);
        });
    }

    private void RefreshDisks()
    {
        var sorted = SortDisks(_allDisks, DiskSort, DiskSortDescending);
        Dispatcher.UIThread.Post(() =>
        {
            Disks.Clear();
            foreach (var disk in sorted) Disks.Add(disk);
        });
    }

    private static IEnumerable<ServerStatsService.ProcessInfo> SortProcesses(
        IEnumerable<ServerStatsService.ProcessInfo> source, ProcessSort sort, bool descending)
    {
        var query = sort switch
        {
            ProcessSort.Cpu => source.OrderBy(p => p.CpuPercent).ThenBy(p => p.MemPercent),
            ProcessSort.Memory => source.OrderBy(p => p.RssBytes).ThenBy(p => p.CpuPercent),
            ProcessSort.Pid => source.OrderBy(p => long.TryParse(p.Pid, out var pid) ? pid : long.MaxValue),
            ProcessSort.Name => source.OrderBy(p => p.Command, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(p => p.CpuPercent)
        };
        return descending ? query.Reverse() : query;
    }

    private static IEnumerable<ServerStatsService.DiskVolumeInfo> SortDisks(
        IEnumerable<ServerStatsService.DiskVolumeInfo> source, string sort, bool descending)
    {
        var query = sort switch
        {
            "Filesystem" => source.OrderBy(d => d.Filesystem, StringComparer.OrdinalIgnoreCase),
            "Size" => source.OrderBy(d => d.SizeBytes),
            "Used" => source.OrderBy(d => d.UsedBytes),
            "Available" => source.OrderBy(d => d.AvailableBytes),
            "UsePercent" => source.OrderBy(d => d.UsePercent),
            _ => source.OrderBy(d => d.MountedOn, StringComparer.OrdinalIgnoreCase)
        };
        return descending ? query.Reverse() : query;
    }

    // ---- Stats collection ----
    private void CollectStats_Execute()
    {
        if (IsCollecting || SelectedProfile == null) return;

        _collectCancellation?.Cancel();
        _collectCancellation = new CancellationTokenSource();
        var cancellation = _collectCancellation;
        var profile = SelectedProfile;

        IsCollecting = true;
        Status = $"Collecting stats from {profile.Name}...";
        StatusColor = "#f39c12";

        _ = Task.Run(async () =>
        {
            try
            {
                var stats = await _statsService.CollectAsync(profile, cancellation.Token);

                Dispatcher.UIThread.Post(() => ApplyStats(stats));
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Stat collection cancelled.";
                    StatusColor = "#f39c12";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Failed to collect stats: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsCollecting = false);
            }
        });
    }

    private void ApplyStats(ServerStatsService.ServerStats stats)
    {
        Host = stats.Host;
        OsInfo = string.IsNullOrWhiteSpace(stats.OsInfo) ? "—" : stats.OsInfo;
        Kernel = string.IsNullOrWhiteSpace(stats.Kernel) ? "—" : stats.Kernel;
        Uptime = string.IsNullOrWhiteSpace(stats.Uptime) ? "—" : stats.Uptime;
        LoadAverage = string.IsNullOrWhiteSpace(stats.LoadAverage) ? "—" : stats.LoadAverage;
        CpuUsagePercent = stats.CpuUsagePercent;

        if (stats.Memory != null && stats.Memory.TotalBytes > 0)
        {
            MemoryTotal = stats.Memory.TotalDisplay;
            MemoryUsed = stats.Memory.UsedDisplay;
            MemoryAvailable = stats.Memory.AvailableDisplay;
            MemoryUsedPercent = stats.Memory.UsedPercent;
        }
        else
        {
            MemoryTotal = MemoryUsed = MemoryAvailable = "—";
            MemoryUsedPercent = -1;
        }

        // Refresh CPU sort first, then memory, so the summary reflects the final count.
        _allCpuProcesses.Clear();
        _allCpuProcesses.AddRange(stats.TopCpuProcesses);
        _allMemoryProcesses.Clear();
        _allMemoryProcesses.AddRange(stats.TopMemoryProcesses);
        _allDisks.Clear();
        _allDisks.AddRange(stats.Disks);

        RefreshCpuProcesses();
        RefreshMemoryProcesses();
        RefreshDisks();

        // Disk summary — aggregate across all volumes (excluding 0-size pseudo filesystems).
        var realDisks = stats.Disks.Where(d => d.SizeBytes > 0).ToList();
        if (realDisks.Count > 0)
        {
            _diskTotalBytes = realDisks.Sum(d => d.SizeBytes);
            _diskUsedBytes = realDisks.Sum(d => d.UsedBytes);
            _diskAvailableBytes = realDisks.Sum(d => d.AvailableBytes);
            DiskUsedPercent = _diskTotalBytes > 0
                ? Math.Round(_diskUsedBytes * 100.0 / _diskTotalBytes, 1)
                : -1;
            DiskSummary =
                $"{ServerStatsService.FormatBytes(_diskUsedBytes)} of {ServerStatsService.FormatBytes(_diskTotalBytes)} used ({DiskUsedPercent:F1}%) · " +
                $"{ServerStatsService.FormatBytes(_diskAvailableBytes)} available across {realDisks.Count} volume(s)";
        }
        else
        {
            _diskTotalBytes = _diskUsedBytes = _diskAvailableBytes = 0;
            DiskUsedPercent = -1;
            DiskSummary = stats.Disks.Count == 0 ? "No disk volumes found" : "Disk usage unavailable";
        }

        Status = $"Stats collected for {stats.Host}.";
        StatusColor = "#4caf50";
    }

    // ---- Shared helpers ----
    private static string UsageColor(double percent, double warnThreshold, double criticalThreshold)
    {
        if (percent < 0) return "#888888";
        if (percent < warnThreshold) return "#4caf50";
        if (percent < criticalThreshold) return "#f39c12";
        return "#f44336";
    }

    private static string BarWidth(double percent)
        => percent < 0 ? "0%" : $"{Math.Clamp(percent, 0, 100):F1}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

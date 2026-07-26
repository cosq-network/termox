using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;

namespace Termox.ViewModels;

public sealed class PortScannerTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly Action<PortScannerTabViewModel> _onClose;
    private string _scanHost = "localhost";
    private string _scanPorts = "22,80,443,3306,5432";
    private bool _isScanning;
    private string _scanStatus = "Ready to scan";

    public string Title => "Port Scanner";

    public ObservableCollection<PortScannerHostOption> HostOptions { get; } = new();

    private PortScannerHostOption? _selectedHost;
    public PortScannerHostOption? SelectedHost
    {
        get => _selectedHost;
        set
        {
            _selectedHost = value;
            if (value != null) ScanHost = value.Host;
            OnPropertyChanged();
        }
    }

    public string ScanHost
    {
        get => _scanHost;
        set { _scanHost = value; OnPropertyChanged(); }
    }

    public string ScanPorts
    {
        get => _scanPorts;
        set { _scanPorts = value; OnPropertyChanged(); }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set { _isScanning = value; OnPropertyChanged(); }
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set { _scanStatus = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PortScanResult> ScanResults { get; } = new();

    public ICommand ScanPortsCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }

    public PortScannerTabViewModel(Action<PortScannerTabViewModel> onClose,
        IEnumerable<SshConnectionProfile>? savedConnections = null)
    {
        _onClose = onClose;
        HostOptions.Add(new PortScannerHostOption("Localhost", "localhost"));
        foreach (var profile in (savedConnections ?? Enumerable.Empty<SshConnectionProfile>())
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Host))
            .GroupBy(profile => profile.Host, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            HostOptions.Add(new PortScannerHostOption(profile.Name, profile.Host));
        }
        SelectedHost = HostOptions[0];
        ScanPortsCommand = new RelayCommand(ScanPorts_Execute, () => !IsScanning);
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => _onClose(this));
    }

    private void ScanPorts_Execute()
    {
        if (IsScanning || string.IsNullOrWhiteSpace(ScanHost)) return;

        var ports = ScanPorts.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p.Trim(), out var port) ? port : -1)
            .Where(port => port is >= 1 and <= 65535)
            .Distinct()
            .ToList();

        if (ports.Count == 0)
        {
            ScanResults.Clear();
            ScanStatus = "Enter valid TCP ports between 1 and 65535.";
            ScanResults.Add(new PortScanResult
            {
                Port = 0,
                Status = "ERROR: Enter one or more ports from 1 to 65535.",
                StatusColor = "#f44336"
            });
            return;
        }

        IsScanning = true;
        ScanResults.Clear();
        var host = ScanHost.Trim();
        ScanStatus = $"Scanning {host}...";
        var scannedCount = 0;
        var openCount = 0;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var port in ports)
                {
                    var isOpen = await IsPortOpen(host, port);
                    Dispatcher.UIThread.Post(() => ScanResults.Add(new PortScanResult
                    {
                        Port = port,
                        Status = isOpen ? "OPEN" : "CLOSED",
                        StatusColor = isOpen ? "#4caf50" : "#999999"
                    }));
                    scannedCount++;
                    if (isOpen) openCount++;
                    Dispatcher.UIThread.Post(() => ScanStatus =
                        $"{scannedCount} of {ports.Count} ports scanned • {openCount} open");
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ScanResults.Add(new PortScanResult
                {
                    Port = 0,
                    Status = $"ERROR: {ex.Message}",
                    StatusColor = "#f44336"
                }));
                Dispatcher.UIThread.Post(() => ScanStatus = $"Scan failed: {ex.Message}");
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsScanning = false;
                    if (scannedCount == ports.Count)
                        ScanStatus = $"Scan complete • {openCount} open of {ports.Count} ports";
                });
            }
        });
    }

    private static async Task<bool> IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

public sealed class PortScannerHostOption
{
    public string DisplayName { get; }
    public string Host { get; }

    public PortScannerHostOption(string displayName, string host)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? host : displayName;
        Host = host;
    }
}

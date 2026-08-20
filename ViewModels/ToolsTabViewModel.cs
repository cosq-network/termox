using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Renci.SshNet;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

public class ToolsTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private string _title = "Network & SSH Tools";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    // Shared command support
    public ICommand DisconnectCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; private set; } = new RelayCommand(() => { });

    // Port Scanner Properties
    private string _scanHost = "localhost";
    public string ScanHost
    {
        get => _scanHost;
        set { _scanHost = value; OnPropertyChanged(); }
    }

    private string _scanPorts = "22,80,443,3306,5432";
    public string ScanPorts
    {
        get => _scanPorts;
        set { _scanPorts = value; OnPropertyChanged(); }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PortScanResult> ScanResults { get; } = new();

    // Ping Properties
    private string _pingHost = "8.8.8.8";
    public string PingHost
    {
        get => _pingHost;
        set { _pingHost = value; OnPropertyChanged(); }
    }

    private int _pingCount = 4;
    public int PingCount
    {
        get => _pingCount;
        set { _pingCount = value; OnPropertyChanged(); }
    }

    private bool _isPinging;
    public bool IsPinging
    {
        get => _isPinging;
        set { _isPinging = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PingResult> PingResults { get; } = new();

    // SSH Key Generator Properties
    private int _keySize = 2048;
    public int KeySize
    {
        get => _keySize;
        set { _keySize = value; OnPropertyChanged(); }
    }

    private string _selectedKeyType = "RSA";
    public string SelectedKeyType
    {
        get => _selectedKeyType;
        set { _selectedKeyType = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRsaKeySelected)); }
    }

    public IReadOnlyList<string> KeyTypes { get; } = ["RSA", "ED25519"];
    public bool IsRsaKeySelected => string.Equals(SelectedKeyType, "RSA", StringComparison.OrdinalIgnoreCase);

    private string _generatedPublicKey = "";
    public string GeneratedPublicKey
    {
        get => _generatedPublicKey;
        set { _generatedPublicKey = value; OnPropertyChanged(); }
    }

    private string _generatedPrivateKey = "";
    public string GeneratedPrivateKey
    {
        get => _generatedPrivateKey;
        set { _generatedPrivateKey = value; OnPropertyChanged(); }
    }

    private bool _isGeneratingKey;
    public bool IsGeneratingKey
    {
        get => _isGeneratingKey;
        set { _isGeneratingKey = value; OnPropertyChanged(); }
    }

    // Connection Tester Properties
    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set { _isTesting = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ConnectionTestResult> TestResults { get; } = new();

    // Speed Test Properties
    private string _speedTestHost = "";
    public string SpeedTestHost
    {
        get => _speedTestHost;
        set { _speedTestHost = value; OnPropertyChanged(); }
    }

    private int _speedTestSize = 10; // MB
    public int SpeedTestSize
    {
        get => _speedTestSize;
        set { _speedTestSize = value; OnPropertyChanged(); }
    }

    private bool _isSpeedTesting;
    public bool IsSpeedTesting
    {
        get => _isSpeedTesting;
        set { _isSpeedTesting = value; OnPropertyChanged(); }
    }

    private string _speedTestResult = "";
    public string SpeedTestResult
    {
        get => _speedTestResult;
        set { _speedTestResult = value; OnPropertyChanged(); }
    }

    // Commands
    public ICommand ScanPortsCommand { get; }
    public ICommand PingCommand { get; }
    public ICommand GenerateKeyCommand { get; }
    public ICommand CopyPublicKeyCommand { get; }
    public ICommand CopyPrivateKeyCommand { get; }
    public ICommand TestAllConnectionsCommand { get; }
    public ICommand RunSpeedTestCommand { get; }

    public ToolsTabViewModel(Action<ToolsTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => { onClose(this); });
        ScanPortsCommand = new RelayCommand(ScanPorts_Execute);
        PingCommand = new RelayCommand(Ping_Execute);
        GenerateKeyCommand = new RelayCommand(GenerateKey_Execute);
        CopyPublicKeyCommand = new RelayCommand(() => _ = CopyToClipboard(GeneratedPublicKey));
        CopyPrivateKeyCommand = new RelayCommand(() => _ = CopyToClipboard(GeneratedPrivateKey));
        TestAllConnectionsCommand = new RelayCommand(TestAllConnections_Execute);
        RunSpeedTestCommand = new RelayCommand(RunSpeedTest_Execute);
    }

    // Port Scanner Implementation
    private void ScanPorts_Execute()
    {
        if (string.IsNullOrWhiteSpace(ScanHost)) return;

        IsScanning = true;
        ScanResults.Clear();

        Task.Run(async () =>
        {
            try
            {
                var ports = ScanPorts.Split(',')
                    .Select(p => p.Trim())
                    .Where(p => int.TryParse(p, out _))
                    .Select(int.Parse)
                    .Where(port => port is >= 1 and <= 65535)
                    .Distinct()
                    .ToList();

                if (ports.Count == 0)
                    throw new ArgumentException("Enter one or more TCP ports from 1 to 65535.");

                foreach (var port in ports)
                {
                    if (IsScanning == false) break;

                    bool isOpen = await IsPortOpen(ScanHost, port);
                    Dispatcher.UIThread.Post(() =>
                    {
                        ScanResults.Add(new PortScanResult
                        {
                            Port = port,
                            Status = isOpen ? "OPEN" : "CLOSED",
                            StatusColor = isOpen ? "#4caf50" : "#999999"
                        });
                    });

                    await Task.Delay(100); // Small delay between checks
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ScanResults.Add(new PortScanResult
                    {
                        Port = 0,
                        Status = $"ERROR: {ex.Message}",
                        StatusColor = "#f44336"
                    });
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsScanning = false);
            }
        });
    }

    private async Task<bool> IsPortOpen(string host, int port, int timeout = 2000)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(host, port);
                var delayTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(connectTask, delayTask);

                return completedTask == connectTask && client.Connected;
            }
        }
        catch
        {
            return false;
        }
    }

    // Ping Implementation
    private void Ping_Execute()
    {
        if (string.IsNullOrWhiteSpace(PingHost)) return;

        IsPinging = true;
        PingResults.Clear();

        Task.Run(async () =>
        {
            try
            {
                using (var ping = new Ping())
                {
                    for (int i = 0; i < PingCount; i++)
                    {
                        if (!IsPinging) break;

                        try
                        {
                            var reply = ping.Send(PingHost, 5000);
                            Dispatcher.UIThread.Post(() =>
                            {
                                string status = reply.Status == IPStatus.Success
                                    ? $"{reply.RoundtripTime}ms"
                                    : "Timeout/Unreachable";

                                string color = reply.Status == IPStatus.Success ? "#4caf50" : "#f44336";

                                PingResults.Add(new PingResult
                                {
                                    Sequence = i + 1,
                                    Response = status,
                                    ResponseColor = color
                                });
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                PingResults.Add(new PingResult
                                {
                                    Sequence = i + 1,
                                    Response = $"Error: {ex.Message}",
                                    ResponseColor = "#f44336"
                                });
                            });
                        }

                        if (i < PingCount - 1)
                            await Task.Delay(1000);
                    }
                }
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsPinging = false);
            }
        });
    }

    // SSH Key Generator Implementation
    private void GenerateKey_Execute()
    {
        IsGeneratingKey = true;
        GeneratedPublicKey = "Generating key...";
        GeneratedPrivateKey = "Generating key...";

        Task.Run(() =>
        {
            try
            {
                var keyPair = GenerateKeyPair();
                Dispatcher.UIThread.Post(() =>
                {
                    GeneratedPublicKey = keyPair.PublicKey;
                    GeneratedPrivateKey = keyPair.PrivateKey;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    GeneratedPublicKey = $"Key generation failed: {ex.Message}";
                    GeneratedPrivateKey = "Make sure the OpenSSH ssh-keygen command is installed and available on PATH.";
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsGeneratingKey = false);
            }
        });
    }

    private (string PublicKey, string PrivateKey) GenerateKeyPair()
    {
        var keyType = string.Equals(SelectedKeyType, "ED25519", StringComparison.OrdinalIgnoreCase)
            ? "ed25519"
            : "rsa";
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"termox-ssh-key-{Guid.NewGuid():N}");
        var privateKeyPath = Path.Combine(temporaryDirectory, "id_key");
        var publicKeyPath = $"{privateKeyPath}.pub";

        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "ssh-keygen.exe" : "ssh-keygen",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-q");
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(keyType);
            if (keyType == "rsa")
            {
                startInfo.ArgumentList.Add("-b");
                startInfo.ArgumentList.Add(Math.Clamp(KeySize, 1024, 4096).ToString());
            }
            startInfo.ArgumentList.Add("-N");
            startInfo.ArgumentList.Add(string.Empty);
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add("termox-generated-key");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(privateKeyPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start ssh-keygen.");
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"ssh-keygen exited with code {process.ExitCode}."
                    : error.Trim());
            }

            return (File.ReadAllText(publicKeyPath).Trim(), File.ReadAllText(privateKeyPath).Trim());
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    // Connection Batch Tester
    public void SetConnections(ObservableCollection<SshConnectionProfile> connections)
    {
        _connections = connections;
    }

    private ObservableCollection<SshConnectionProfile> _connections = new();

    private void TestAllConnections_Execute()
    {
        if (_connections.Count == 0) return;

        IsTesting = true;
        TestResults.Clear();

        Task.Run(async () =>
        {
            foreach (var profile in _connections)
            {
                if (!IsTesting) break;

                var result = await TestConnection(profile);
                Dispatcher.UIThread.Post(() => TestResults.Add(result));
                await Task.Delay(500);
            }

            Dispatcher.UIThread.Post(() => IsTesting = false);
        });
    }

    private async Task<ConnectionTestResult> TestConnection(SshConnectionProfile profile)
    {
        var startTime = DateTime.Now;

        try
        {
            SshClient testClient;
            var safeUsername = profile.Username ?? "";
            var safePassword = profile.Password ?? "";
            SshSecurity.EnsurePrivateKeyExists(profile.PrivateKeyPath);

            if (!string.IsNullOrWhiteSpace(profile.PrivateKeyPath) && System.IO.File.Exists(profile.PrivateKeyPath))
            {
                var keyFile = new PrivateKeyFile(profile.PrivateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                testClient = new SshClient(profile.Host ?? "", profile.Port, safeUsername, new[] { keyFile });
            }

            else
            {
                testClient = new SshClient(profile.Host ?? "", profile.Port, safeUsername, safePassword);
            }

            testClient.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;

            SshSecurity.ConfigureHostKeyPolicy(testClient, profile.HostKeyFingerprint, null);

            var connectTask = Task.Run(() =>
            {
                try
                {
                    testClient.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                finally
                {
                    try { testClient.Disconnect(); } catch { }
                    testClient.Dispose();
                }
            });

            var delayTask = Task.Delay(10000); // 10 second timeout
            var completed = await Task.WhenAny(connectTask, delayTask);

            var elapsed = DateTime.Now - startTime;

            if (completed == delayTask)
            {
                try { testClient.Dispose(); } catch { }
                _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return new ConnectionTestResult
                {
                    ProfileName = profile.Name,
                    Status = "TIMEOUT",
                    ResponseTime = "10000ms+",
                    StatusColor = "#f39c12"
                };
            }

            await connectTask;

            return new ConnectionTestResult
            {
                ProfileName = profile.Name,
                Status = "SUCCESS",
                ResponseTime = $"{elapsed.TotalMilliseconds:F0}ms",
                StatusColor = "#4caf50"
            };
        }
        catch (Exception ex)
        {
            var elapsed = DateTime.Now - startTime;
            return new ConnectionTestResult
            {
                ProfileName = profile.Name,
                Status = "FAILED",
                ResponseTime = ex.Message,
                StatusColor = "#f44336"
            };
        }
    }

    // SSH endpoint latency test. A real transfer benchmark requires an authenticated SFTP session.
    private void RunSpeedTest_Execute()
    {
        if (string.IsNullOrWhiteSpace(SpeedTestHost)) return;

        IsSpeedTesting = true;
        SpeedTestResult = "Starting speed test...";

        Task.Run(async () =>
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var client = new TcpClient();
                await client.ConnectAsync(SpeedTestHost, 22).WaitAsync(TimeSpan.FromSeconds(10));
                stopwatch.Stop();
                var result = $"SSH endpoint reachable\nHost: {SpeedTestHost}\nPort: 22\nConnection latency: {stopwatch.ElapsedMilliseconds} ms\n\nAuthenticated SFTP transfer benchmarking requires an active SFTP session.";
                Dispatcher.UIThread.Post(() => SpeedTestResult = result);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => SpeedTestResult = $"Error: {ex.Message}");
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsSpeedTesting = false);
            }
        });
    }

    private async Task CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

// Supporting Classes
public class PortScanResult
{
    public int Port { get; set; }
    public string Status { get; set; } = "";
    public string StatusColor { get; set; } = "#999999";
}

public class PingResult
{
    public int Sequence { get; set; }
    public string Response { get; set; } = "";
    public string ResponseColor { get; set; } = "#999999";
}

public class ConnectionTestResult
{
    public string ProfileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ResponseTime { get; set; } = "";
    public string StatusColor { get; set; } = "#999999";
}

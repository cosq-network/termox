using SvcSystems.UI.Terminal;
using Renci.SshNet;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Services;

namespace Termox.ViewModels;

public class TerminalTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private readonly object _connectionLock = new();
    private CancellationTokenSource? _connectionCancellation;
    private readonly object _directoryProbeLock = new();
    private TaskCompletionSource<string?>? _directoryProbe;
    private string? _directoryProbeMarker;
    private string _directoryProbeBuffer = "";

    public TerminalControlModel TerminalModel { get; } = new TerminalControlModel();

    private string _title = "New Tab";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public string ConnectionHost { get; private set; } = "";
    public int ConnectionPort { get; private set; } = 22;
    public string ConnectionUsername { get; private set; } = "";
    public string? ConnectionProfileId { get; set; }

    private string _status = "Disconnected";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private string _statusColor = "#888888";
    public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }

    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }

    public async Task<string?> GetCurrentDirectoryAsync()
    {
        ShellStream? shell;
        TaskCompletionSource<string?> probe;
        string marker;

        lock (_connectionLock)
        {
            shell = _shellStream;
            if (_sshClient == null || !_sshClient.IsConnected || shell == null)
                return null;
        }

        lock (_directoryProbeLock)
        {
            if (_directoryProbe != null) return null;
            marker = $"__TERMOX_PWD_{Guid.NewGuid():N}__";
            probe = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _directoryProbe = probe;
            _directoryProbeMarker = marker;
            _directoryProbeBuffer = "";
        }

        try
        {
            // Split the marker in the shell command so it does not occur
            // contiguously in the command echo. The parser can then only
            // match the marker from printf's actual output.
            var commandMarker = marker.Replace("PWD", "'PWD", StringComparison.Ordinal) + "'";
            var command = Encoding.UTF8.GetBytes($"printf '\\n{commandMarker}%s\\n' \"$PWD\"\n");
            shell.Write(command, 0, command.Length);
            shell.Flush();

            var completed = await Task.WhenAny(probe.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            return completed == probe.Task ? await probe.Task : null;
        }
        finally
        {
            lock (_directoryProbeLock)
            {
                if (ReferenceEquals(_directoryProbe, probe))
                {
                    _directoryProbe = null;
                    _directoryProbeMarker = null;
                    _directoryProbeBuffer = "";
                }
            }
        }
    }

    public TerminalTabViewModel(Action<TerminalTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(Disconnect);
        CloseTabCommand = new RelayCommand(() => { Disconnect(); onClose(this); });

        TerminalModel.UserInput += (_, e) =>
        {
            if (_shellStream != null && _sshClient != null && _sshClient.IsConnected)
            {
                var bytes = e.Data.ToArray();
                _shellStream.Write(bytes, 0, bytes.Length);
                _shellStream.Flush();
            }
        };
    }

    public void Connect(string host, int port, string username, string password, string privateKeyPath,
        string? hostKeyFingerprint = null, Action<string>? firstSeenHostKey = null,
        string? initialCommand = null)
    {
        lock (_connectionLock)
        {
            _connectionCancellation?.Cancel();
            _connectionCancellation = new CancellationTokenSource();
        }
        var cancellation = _connectionCancellation;
        ConnectionHost = host;
        ConnectionPort = port;
        ConnectionUsername = username;
        Title = host;
        Status = "Connecting...";
        StatusColor = "#f39c12";

        Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\r\n\u001b[33m[Termox] Connecting to {host} on port {port}...\u001b[0m\r\n"));

        Task.Run(() =>
        {
            SshClient? client = null;
            var connected = false;
            try
            {
                SshSecurity.EnsurePrivateKeyExists(privateKeyPath);
                var safeUsername = username ?? "";
                var safePassword = password ?? "";

                if (!string.IsNullOrWhiteSpace(privateKeyPath) && System.IO.File.Exists(privateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(privateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    client = new SshClient(host, port, safeUsername, new[] { keyFile });
                }
                else
                {
                    client = new SshClient(host, port, safeUsername, safePassword);
                }

                client.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;
                SshSecurity.ConfigureHostKeyPolicy(client, hostKeyFingerprint, firstSeenHostKey);
                lock (_connectionLock)
                {
                    if (cancellation.IsCancellationRequested) return;
                    _sshClient = client;
                }

                client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
                connected = true;

                _shellStream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

                if (!string.IsNullOrWhiteSpace(initialCommand))
                {
                    var commandBytes = Encoding.UTF8.GetBytes(initialCommand.EndsWith("\n", StringComparison.Ordinal)
                        ? initialCommand
                        : initialCommand + "\n");
                    _shellStream.Write(commandBytes, 0, commandBytes.Length);
                    _shellStream.Flush();
                }

                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Connected to " + host;
                    StatusColor = "#4caf50";
                });

                Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\u001b[32m[Termox] Connection established successfully.\u001b[0m\r\n"));

                _ = ReadOutputAsync();
            }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested) return;
                lock (_connectionLock)
                {
                    if (ReferenceEquals(_sshClient, client)) _sshClient = null;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Error: " + ex.Message;
                    StatusColor = "#f44336";
                    TerminalModel.Feed($"\u001b[31m[Termox] Connection Failed: {ex.Message}\u001b[0m\r\n");
                });
            }
            finally
            {
                if (!connected)
                {
                    try { client?.Dispose(); } catch { }
                }
            }
        });
    }

    private void Disconnect()
    {
        lock (_connectionLock) _connectionCancellation?.Cancel();
        try
        {
            ShellStream? shell;
            SshClient? client;
            lock (_connectionLock)
            {
                shell = _shellStream;
                client = _sshClient;
                _shellStream = null;
                _sshClient = null;
            }
            try { client?.Disconnect(); } catch { }
            shell?.Dispose();
            client?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during disconnect: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = "Disconnected";
                StatusColor = "#888888";
            });
        }
    }

    private async Task ReadOutputAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                ShellStream? shell;
                SshClient? client;
                lock (_connectionLock)
                {
                    shell = _shellStream;
                    client = _sshClient;
                }

                if (shell == null || client == null || !client.IsConnected)
                    break;

                int read;
                try
                {
                    read = await shell.ReadAsync(buffer, 0, buffer.Length);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (read > 0)
                {
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    ResolveDirectoryProbe(text);
                    Dispatcher.UIThread.Post(() => TerminalModel.Feed(text));
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading output: {ex.Message}");
        }
        Disconnect();
    }

    private void ResolveDirectoryProbe(string text)
    {
        lock (_directoryProbeLock)
        {
            if (_directoryProbe == null || string.IsNullOrWhiteSpace(_directoryProbeMarker)) return;

            _directoryProbeBuffer += text;
            // Interactive shells echo the probe command before printing its
            // result. The last marker is therefore the actual $PWD output.
            var markerIndex = _directoryProbeBuffer.LastIndexOf(_directoryProbeMarker, StringComparison.Ordinal);
            if (markerIndex < 0) return;

            var pathStart = markerIndex + _directoryProbeMarker.Length;
            var lineEnd = _directoryProbeBuffer.IndexOfAny(new[] { '\r', '\n' }, pathStart);
            if (lineEnd < 0) return;

            var path = _directoryProbeBuffer[pathStart..lineEnd].Trim();
            _directoryProbe.TrySetResult(string.IsNullOrWhiteSpace(path) ? null : path);
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

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
        string? hostKeyFingerprint = null, Action<string>? firstSeenHostKey = null)
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

                Status = "Connected to " + host;
                StatusColor = "#4caf50";

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
                Status = "Error: " + ex.Message;
                StatusColor = "#f44336";
                Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\u001b[31m[Termox] Connection Failed: {ex.Message}\u001b[0m\r\n"));
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
            shell?.Dispose();
            client?.Disconnect();
            client?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during disconnect: {ex.Message}");
        }
        finally
        {
            Status = "Disconnected";
            StatusColor = "#888888";
        }
    }

    private async Task ReadOutputAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (_sshClient != null && _sshClient.IsConnected && _shellStream != null)
            {
                int read = await _shellStream.ReadAsync(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

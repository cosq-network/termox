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
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleWarningLead = TimeSpan.FromSeconds(60);
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private readonly object _connectionLock = new();
    private CancellationTokenSource? _connectionCancellation;
    private readonly object _directoryProbeLock = new();
    private TaskCompletionSource<string?>? _directoryProbe;
    private string? _directoryProbeMarker;
    private string _directoryProbeBuffer = "";
    private long _lastInputTimestamp;
    private int _rapidZeroInputCount;
    private CancellationTokenSource? _idleMonitorCancellation;
    private long _lastActivityTimestamp;
    private string _password = "";
    private string _privateKeyPath = "";
    private string? _privateKeyPassphrase;
    private string? _hostKeyFingerprint;
    private Action<string>? _firstSeenHostKey;
    private Func<string, bool>? _confirmNewHost;
    private string? _initialCommand;
    private string? _disconnectReason;
    private int _retryCount = 3;
    private int _retryDelayMs = 2000;
    private int _keepAliveSeconds = 60;
    private int _idleTimeoutMinutes;
    private bool _idleWarningShown;

    public TerminalControlModel TerminalModel { get; } = new TerminalControlModel(new TerminalOptions
    {
        // Keep the terminal buffer aligned with the available viewport so long
        // commands reflow instead of requiring horizontal scrolling.
        ReflowOnResize = true
    });

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

    private bool _isReconnectAvailable;
    public bool IsReconnectAvailable
    {
        get => _isReconnectAvailable;
        private set { _isReconnectAvailable = value; OnPropertyChanged(); }
    }

    public ICommand DisconnectCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ClearHistoryCommand { get; }

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
        ReconnectCommand = new RelayCommand(Reconnect);
        CloseTabCommand = new RelayCommand(() => { Disconnect(); onClose(this); });
        ClearHistoryCommand = new RelayCommand(ClearHistory);

        TerminalModel.SizeChanged += (_, _) =>
        {
            var terminal = TerminalModel.Terminal;
            ResizeRemoteTerminal(terminal.Cols, terminal.Rows);
        };

        TerminalModel.UserInput += (_, e) =>
        {
            var bytes = NormalizeTerminalInput(e.Data.ToArray());

            // Some key combinations can be reported by the terminal
            // control as a runaway stream of single ASCII '0' bytes.
            // Keep ordinary typing and paste intact, but stop that
            // pathological repeat before it reaches the remote shell.
            if (IsRunawayZeroInput(bytes))
                return;

            lock (_connectionLock)
            {
                if (_shellStream == null || _sshClient == null || !_sshClient.IsConnected)
                    return;

                try
                {
                    MarkActivity();
                    _shellStream.Write(bytes, 0, bytes.Length);
                    _shellStream.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Terminal input write failed: {ex.Message}");
                }
            }
        };
    }

    private static byte[] NormalizeTerminalInput(byte[] bytes)
    {
        if (Array.IndexOf(bytes, (byte)'\n') < 0)
            return bytes;

        var normalized = new byte[bytes.Length];
        var length = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value == (byte)'\n')
            {
                // A CRLF from the clipboard is one Enter, not two.
                if (length > 0 && normalized[length - 1] == (byte)'\r')
                    continue;

                normalized[length++] = (byte)'\r';
            }
            else
            {
                normalized[length++] = value;
            }
        }

        return normalized[..length];
    }

    private bool IsRunawayZeroInput(byte[] bytes)
    {
        var now = Environment.TickCount64;
        if (bytes.Length != 1 || bytes[0] != (byte)'0' || now - _lastInputTimestamp > 250)
        {
            _lastInputTimestamp = now;
            _rapidZeroInputCount = 0;
            return false;
        }

        _lastInputTimestamp = now;
        _rapidZeroInputCount++;
        return _rapidZeroInputCount > 8;
    }

    public void Connect(string host, int port, string username, string password, string privateKeyPath,
        string? privateKeyPassphrase = null, string? hostKeyFingerprint = null,
        Action<string>? firstSeenHostKey = null, Func<string, bool>? confirmNewHost = null,
        string? initialCommand = null, int? retryCount = null, int? retryDelayMs = null,
        int? keepAliveSeconds = null, int? idleTimeoutMinutes = null)
    {
        lock (_connectionLock)
        {
            _connectionCancellation?.Cancel();
            _idleMonitorCancellation?.Cancel();
            _connectionCancellation = new CancellationTokenSource();
        }
        var cancellation = _connectionCancellation;
        ConnectionHost = host;
        ConnectionPort = port;
        ConnectionUsername = username;
        _password = password;
        _privateKeyPath = privateKeyPath;
        _privateKeyPassphrase = privateKeyPassphrase;
        _hostKeyFingerprint = hostKeyFingerprint;
        _firstSeenHostKey = firstSeenHostKey;
        _confirmNewHost = confirmNewHost;
        _initialCommand = initialCommand;
        _disconnectReason = null;
        _retryCount = Math.Max(1, retryCount ?? 3);
        _retryDelayMs = Math.Max(0, retryDelayMs ?? 2000);
        _keepAliveSeconds = Math.Max(0, keepAliveSeconds ?? 60);
        _idleTimeoutMinutes = Math.Max(0, idleTimeoutMinutes ?? 0);
        _idleWarningShown = false;
        IsReconnectAvailable = false;
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
                client = SshConnectionFactory.CreateSshClient(
                    host, port, username ?? "", password ?? "",
                    privateKeyPath, privateKeyPassphrase, _keepAliveSeconds);

                SshSecurity.ConfigureHostKeyPolicy(client, hostKeyFingerprint, firstSeenHostKey, confirmNewHost);
                lock (_connectionLock)
                {
                    if (cancellation.IsCancellationRequested) return;
                    _sshClient = client;
                }

                // Attempt connection with retries
                var maxRetries = _retryCount;
                var retryDelayMs = _retryDelayMs;
                var lastException = (Exception?)null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            Dispatcher.UIThread.Post(() => 
                                TerminalModel.Feed($"\u001b[33m[Termox] Connection attempt {attempt}/{maxRetries}...\u001b[0m\r\n"));
                            Task.Delay(retryDelayMs, cancellation.Token).Wait(cancellation.Token);
                        }

                        client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
                        connected = true;
                        break;  // Successfully connected, exit retry loop
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt == maxRetries)
                            throw;  // Last attempt failed, throw exception
                        
                        // Continue to next retry
                    }
                }

                if (!connected && lastException != null)
                    throw lastException;

                _shellStream = client.CreateShellStream("xterm", (uint)TerminalModel.Terminal.Cols,
                    (uint)TerminalModel.Terminal.Rows, 800, 600, 1024);

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
                    IsReconnectAvailable = false;
                });

                MarkActivity();
                StartIdleMonitor(cancellation.Token);

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

    private void ClearHistory()
    {
        TerminalModel.Terminal.Engine.Clear();
    }

    private void Reconnect()
    {
        if (string.IsNullOrWhiteSpace(ConnectionHost) || string.IsNullOrWhiteSpace(ConnectionUsername))
            return;

        Connect(ConnectionHost, ConnectionPort, ConnectionUsername, _password, _privateKeyPath,
            _privateKeyPassphrase, _hostKeyFingerprint, _firstSeenHostKey, _confirmNewHost,
            _initialCommand, _retryCount, _retryDelayMs, _keepAliveSeconds, _idleTimeoutMinutes);
    }

    private void MarkActivity()
    {
        Interlocked.Exchange(ref _lastActivityTimestamp, Environment.TickCount64);
        _idleWarningShown = false;
    }

    private void StartIdleMonitor(CancellationToken connectionCancellation)
    {
        // Idle timeout of 0 disables the monitor entirely — the session stays open.
        if (_idleTimeoutMinutes <= 0) return;

        var monitorCancellation = new CancellationTokenSource();
        lock (_connectionLock)
        {
            _idleMonitorCancellation?.Cancel();
            _idleMonitorCancellation = monitorCancellation;
        }

        var idleTimeout = TimeSpan.FromMinutes(_idleTimeoutMinutes);
        var warningLead = idleTimeout > IdleWarningLead ? IdleWarningLead : TimeSpan.FromSeconds(Math.Max(5, idleTimeout.TotalSeconds / 2));

        _ = Task.Run(async () =>
        {
            try
            {
                while (!monitorCancellation.IsCancellationRequested && !connectionCancellation.IsCancellationRequested)
                {
                    await Task.Delay(IdleCheckInterval, monitorCancellation.Token);
                    var idleFor = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastActivityTimestamp));

                    if (idleFor < idleTimeout - warningLead)
                        continue;

                    // Warn once, shortly before the disconnect.
                    if (idleFor < idleTimeout && !_idleWarningShown)
                    {
                        _idleWarningShown = true;
                        var minutes = Math.Max(1, (int)Math.Ceiling(idleFor.TotalMinutes));
                        var message = $"\r\n\u001b[33m[Termox] No activity for {minutes} minute(s). " +
                                      $"Session will disconnect in ~{Math.Max(1, (int)Math.Ceiling((idleTimeout - idleFor).TotalSeconds))}s unless you interact.\u001b[0m\r\n";
                        Dispatcher.UIThread.Post(() => TerminalModel.Feed(message));
                        continue;
                    }

                    if (idleFor >= idleTimeout)
                    {
                        Disconnect($"SSH connection closed after {_idleTimeoutMinutes} minute(s) of inactivity.");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the connection closes or reconnects.
            }
            finally
            {
                monitorCancellation.Dispose();
            }
        });
    }

    private void ResizeRemoteTerminal(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0) return;

        lock (_connectionLock)
        {
            if (_shellStream == null || _sshClient == null || !_sshClient.IsConnected) return;

            try
            {
                _shellStream.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Remote terminal resize failed: {ex.Message}");
            }
        }
    }

    private void Disconnect()
    {
        Disconnect(null, preserveExistingReason: false);
    }

    private void Disconnect(string? reason)
    {
        Disconnect(reason, preserveExistingReason: false);
    }

    private void Disconnect(string? reason, bool preserveExistingReason)
    {
        if (reason != null)
            _disconnectReason = reason;
        else if (!preserveExistingReason)
            _disconnectReason = null;

        var displayedReason = reason ?? _disconnectReason;
        var shouldReportReason = displayedReason != null && !preserveExistingReason;
        lock (_connectionLock)
        {
            _connectionCancellation?.Cancel();
            _idleMonitorCancellation?.Cancel();
        }
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
                IsReconnectAvailable = displayedReason != null;
                Status = displayedReason ?? "Disconnected";
                StatusColor = displayedReason != null ? "#f39c12" : "#888888";
                if (shouldReportReason)
                    TerminalModel.Feed($"\r\n\u001b[33m[Termox] {displayedReason} Use Reconnect to restore the SSH session.\u001b[0m\r\n");
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
                    MarkActivity();
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
        Disconnect(null, preserveExistingReason: true);
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

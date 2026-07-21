using SvcSystems.UI.Terminal;
using Renci.SshNet;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace Termox.ViewModels;

public class TerminalTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private SshClient? _sshClient;
    private ShellStream? _shellStream;

    public TerminalControlModel TerminalModel { get; } = new TerminalControlModel();

    private string _title = "New Tab";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

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

    public void Connect(string host, int port, string username, string password, string privateKeyPath)
    {
        Title = host;
        Status = "Connecting...";
        StatusColor = "#f39c12";
        
        Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\r\n\u001b[33m[Termox] Connecting to {host} on port {port}...\u001b[0m\r\n"));

        Task.Run(() =>
        {
            try
            {
                var safeUsername = username ?? "";
                var safePassword = password ?? "";
                
                if (!string.IsNullOrWhiteSpace(privateKeyPath) && System.IO.File.Exists(privateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(privateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    _sshClient = new SshClient(host, port, safeUsername, new[] { keyFile });
                }
                else
                {
                    _sshClient = new SshClient(host, port, safeUsername, safePassword);
                }
                
                _sshClient.Connect();

                _shellStream = _sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

                Status = "Connected to " + host;
                StatusColor = "#4caf50";
                
                Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\u001b[32m[Termox] Connection established successfully.\u001b[0m\r\n"));

                ReadOutputAsync();
            }
            catch (Exception ex)
            {
                Status = "Error: " + ex.Message;
                StatusColor = "#f44336";
                Dispatcher.UIThread.Post(() => TerminalModel.Feed($"\u001b[31m[Termox] Connection Failed: {ex.Message}\u001b[0m\r\n"));
            }
        });
    }

    private void Disconnect()
    {
        try
        {
            _shellStream?.Dispose();
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during disconnect: {ex.Message}");
        }
        finally
        {
            _shellStream = null;
            _sshClient = null;
            Status = "Disconnected";
            StatusColor = "#888888";
        }
    }

    private async void ReadOutputAsync()
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

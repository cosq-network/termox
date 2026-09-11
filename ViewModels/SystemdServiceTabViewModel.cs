using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

/// <summary>
/// Tools tab for starting/stopping/restarting a systemd service on a saved SSH host.
/// Every action runs a systemctl command remotely through SystemdService and shows the
/// raw output — holds no local service state.
/// </summary>
public class SystemdServiceTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly Action<SystemdServiceTabViewModel> _onClose;
    private readonly SystemdService _systemdService = new();

    public string Title => "Service Manager";

    public ICommand DisconnectCommand { get; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; }

    public ObservableCollection<SshConnectionProfile> HostProfiles { get; } = new();

    private SshConnectionProfile? _selectedProfile;
    public SshConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    private string _unitName = "";
    public string UnitName
    {
        get => _unitName;
        set { _unitName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Common service names across distros, shown as quick-select chips below the unit
    /// name field — unit names vary (nginx is always nginx, but SSH is "ssh" on Debian/
    /// Ubuntu and "sshd" on RHEL/Fedora/Arch), so this is a starting point, not a fixed
    /// list — the field stays free text.
    /// </summary>
    public string[] CommonUnitNames { get; } =
    {
        "nginx", "apache2", "httpd", "docker", "mysql", "mariadb", "postgresql",
        "redis-server", "mongod", "ssh", "sshd", "cron", "postfix", "php-fpm"
    };

    public ICommand SelectUnitNameCommand { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            (StatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RestartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EnableCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisableCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _status = "Select a saved session and enter a systemd unit name, e.g. nginx.";
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

    private string _output = "";
    public string Output
    {
        get => _output;
        private set { _output = value; OnPropertyChanged(); }
    }

    public ICommand StatusCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }

    public SystemdServiceTabViewModel(Action<SystemdServiceTabViewModel> onClose, IEnumerable<SshConnectionProfile>? savedConnections = null)
    {
        _onClose = onClose;

        foreach (var profile in savedConnections ?? Enumerable.Empty<SshConnectionProfile>())
            HostProfiles.Add(profile);
        SelectedProfile = HostProfiles.FirstOrDefault();
        if (SelectedProfile == null)
        {
            Status = "No saved sessions available. Add a connection first.";
            StatusColor = "#f39c12";
        }

        StatusCommand = new RelayCommand(() => Run("Checking status...", p => _systemdService.StatusAsync(p, UnitName), "Status retrieved."), () => !IsBusy);
        StartCommand = new RelayCommand(() => Run("Starting...", p => _systemdService.StartAsync(p, UnitName), "Started."), () => !IsBusy);
        StopCommand = new RelayCommand(() => Run("Stopping...", p => _systemdService.StopAsync(p, UnitName), "Stopped."), () => !IsBusy);
        RestartCommand = new RelayCommand(() => Run("Restarting...", p => _systemdService.RestartAsync(p, UnitName), "Restarted."), () => !IsBusy);
        EnableCommand = new RelayCommand(() => Run("Enabling...", p => _systemdService.EnableAsync(p, UnitName), "Enabled."), () => !IsBusy);
        DisableCommand = new RelayCommand(() => Run("Disabling...", p => _systemdService.DisableAsync(p, UnitName), "Disabled."), () => !IsBusy);
        SelectUnitNameCommand = new RelayCommand<string>(name => UnitName = name ?? "");
        CloseTabCommand = new RelayCommand(() => _onClose(this));
    }

    private void Run(string startStatus, Func<SshConnectionProfile, Task<string>> action, string successMessage)
    {
        if (IsBusy) return;
        var profile = SelectedProfile;
        if (profile == null)
        {
            Status = "Select a saved session first.";
            StatusColor = "#f39c12";
            return;
        }
        if (string.IsNullOrWhiteSpace(UnitName))
        {
            Status = "Enter a systemd unit name first, e.g. nginx.";
            StatusColor = "#f39c12";
            return;
        }

        IsBusy = true;
        Status = startStatus;
        StatusColor = "#f39c12";

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await action(profile).ConfigureAwait(false);
                var (status, color) = ResultStatus(result, successMessage);
                Dispatcher.UIThread.Post(() =>
                {
                    Output = result;
                    Status = status;
                    StatusColor = color;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Failed: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsBusy = false);
            }
        });
    }

    /// <summary>
    /// A command that ran fine over SSH can still have failed on the remote side — sudo
    /// refusing it, or systemctl reporting the unit doesn't exist or failed to (re)start.
    /// Checked before defaulting to success, built in from day one rather than bolted on
    /// after a bug report like the Certbot and Server Transfer tabs needed earlier.
    /// </summary>
    private static (string Status, string Color) ResultStatus(string result, string successMessage)
    {
        if (SudoFailureDetector.IsSudoFailure(result, out var sudoReason))
            return (sudoReason, "#f44336");

        if (result.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Failed to start", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Failed to stop", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Failed to restart", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Failed to enable", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Failed to disable", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Job for", StringComparison.OrdinalIgnoreCase) && result.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return ("Failed: systemctl could not complete the request — see output below.", "#f44336");
        }

        return (successMessage, "#4caf50");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

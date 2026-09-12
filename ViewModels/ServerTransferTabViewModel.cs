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
/// Tools tab for moving a file directly between two saved SSH servers, in either of two
/// modes: Relay (streamed through this process, works between any two reachable servers)
/// or Direct (rsync/scp run on the source server targeting the destination directly — no
/// data passes through Termox, but requires the source to already have network access and
/// SSH trust to the destination configured out of band).
/// </summary>
public class ServerTransferTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly Action<ServerTransferTabViewModel> _onClose;
    private readonly SftpToolService _sftpToolService = new();

    public string Title => "Server Transfer";

    public ICommand DisconnectCommand { get; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; }

    public ObservableCollection<SshConnectionProfile> HostProfiles { get; } = new();

    private SshConnectionProfile? _sourceProfile;
    public SshConnectionProfile? SourceProfile
    {
        get => _sourceProfile;
        set { _sourceProfile = value; OnPropertyChanged(); }
    }

    private SshConnectionProfile? _destProfile;
    public SshConnectionProfile? DestProfile
    {
        get => _destProfile;
        set
        {
            _destProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirectModePreview));
        }
    }

    private string _sourceRemotePath = "";
    public string SourceRemotePath
    {
        get => _sourceRemotePath;
        set { _sourceRemotePath = value; OnPropertyChanged(); }
    }

    private string _destRemotePath = "";
    public string DestRemotePath
    {
        get => _destRemotePath;
        set { _destRemotePath = value; OnPropertyChanged(); }
    }

    public string[] TransferModes { get; } = { "Relay through Termox", "Direct (advanced)" };

    private string _selectedMode = "Relay through Termox";
    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            _selectedMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirectMode));
        }
    }

    public bool IsDirectMode => SelectedMode == "Direct (advanced)";

    public string DirectModePreview => SourceProfile != null && DestProfile != null
        ? $"Will run on '{SourceProfile.Name}', targeting {DestProfile.Username}@{DestProfile.Host}:{DestProfile.Port} directly. " +
          "Requires the source server to already be able to reach and authenticate to the destination — " +
          "Termox does not configure this or send any stored credential for it."
        : "Pick a source and destination host to preview the direct-mode command target.";

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            (TransferCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _status = "Pick a source and destination host, then transfer a file directly between them.";
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

    public ICommand TransferCommand { get; }

    public ServerTransferTabViewModel(Action<ServerTransferTabViewModel> onClose, IEnumerable<SshConnectionProfile>? savedConnections = null)
    {
        _onClose = onClose;

        foreach (var profile in savedConnections ?? Enumerable.Empty<SshConnectionProfile>())
            HostProfiles.Add(profile);
        SourceProfile = HostProfiles.FirstOrDefault();
        DestProfile = HostProfiles.Skip(1).FirstOrDefault() ?? HostProfiles.FirstOrDefault();
        if (SourceProfile == null)
        {
            Status = "No saved sessions available. Add at least two connections first.";
            StatusColor = "#f39c12";
        }

        TransferCommand = new RelayCommand(Transfer_Execute, () => !IsBusy);
        CloseTabCommand = new RelayCommand(() => _onClose(this));
    }

    private void Transfer_Execute()
    {
        if (IsBusy) return;
        var source = SourceProfile;
        var dest = DestProfile;
        if (source == null || dest == null)
        {
            Status = "Pick both a source and a destination host.";
            StatusColor = "#f39c12";
            return;
        }

        IsBusy = true;
        Status = IsDirectMode ? "Running direct transfer..." : "Transferring...";
        StatusColor = "#f39c12";
        var directMode = IsDirectMode;
        var sourcePath = SourceRemotePath;
        var destPath = DestRemotePath;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = directMode
                    ? await _sftpToolService.TransferDirectAsync(
                        source, sourcePath, dest.Host, dest.Port, dest.Username, destPath).ConfigureAwait(false)
                    : await _sftpToolService.TransferRelayAsync(
                        source, sourcePath, dest, destPath).ConfigureAwait(false);

                var (status, color) = TransferStatus(result);
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
    /// Direct mode's result comes from RunCommandAsync, which returns whatever ssh/scp/rsync
    /// printed as plain text without throwing on a non-zero exit — a connection failure
    /// reads as a normal, non-throwing result. Without this check every Direct-mode failure
    /// (host unreachable, no trust configured, wrong port) would show as a green "Transfer
    /// complete." with the real error only visible if you happen to read the Output box.
    /// </summary>
    private static (string Status, string Color) TransferStatus(string result)
    {
        if (SudoFailureDetector.IsSudoFailure(result, out var sudoReason))
            return (sudoReason, "#f44336");

        if (result.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Connection closed", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("No route to host", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("rsync error", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("rsync: ", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return ("Failed: transfer did not complete — see output below.", "#f44336");
        }

        return ("Transfer complete.", "#4caf50");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

/// <summary>
/// Tools tab for issuing/renewing/revoking TLS certificates via certbot on a saved SSH
/// host. Holds no local ACME/key state — every action runs a certbot command remotely
/// through <see cref="CertbotService"/> and shows the raw output.
/// </summary>
public class CertbotTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly Action<CertbotTabViewModel> _onClose;
    private readonly CertbotService _certbotService = new();

    public string Title => "Certbot";

    public ICommand DisconnectCommand { get; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; }

    public ObservableCollection<SshConnectionProfile> HostProfiles { get; } = new();

    private SshConnectionProfile? _selectedProfile;
    public SshConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    private string _domainsText = "";
    public string DomainsText
    {
        get => _domainsText;
        set { _domainsText = value; OnPropertyChanged(); }
    }

    private string _email = "";
    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string[] Plugins { get; } = { "Standalone", "Webroot", "Nginx", "Apache" };

    private string _selectedPlugin = "Standalone";
    public string SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            _selectedPlugin = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWebrootPluginSelected));
        }
    }

    public bool IsWebrootPluginSelected => SelectedPlugin == "Webroot";

    private string _webrootPath = "";
    public string WebrootPath
    {
        get => _webrootPath;
        set { _webrootPath = value; OnPropertyChanged(); }
    }

    private bool _dryRun = true;
    public bool DryRun
    {
        get => _dryRun;
        set { _dryRun = value; OnPropertyChanged(); }
    }

    private string _certNameToRevoke = "";
    public string CertNameToRevoke
    {
        get => _certNameToRevoke;
        set { _certNameToRevoke = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            (CheckStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ListCertificatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ObtainCertificateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RenewAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RevokeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _status = "Select a saved session, then check whether certbot is installed there.";
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

    public ICommand CheckStatusCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand ListCertificatesCommand { get; }
    public ICommand ObtainCertificateCommand { get; }
    public ICommand RenewAllCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand CopyOutputCommand { get; }
    public ICommand ClearOutputCommand { get; }

    public CertbotTabViewModel(Action<CertbotTabViewModel> onClose, IEnumerable<SshConnectionProfile>? savedConnections = null)
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

        CheckStatusCommand = new RelayCommand(CheckStatus_Execute, () => !IsBusy);
        InstallCommand = new RelayCommand(Install_Execute, () => !IsBusy);
        ListCertificatesCommand = new RelayCommand(ListCertificates_Execute, () => !IsBusy);
        ObtainCertificateCommand = new RelayCommand(ObtainCertificate_Execute, () => !IsBusy);
        RenewAllCommand = new RelayCommand(RenewAll_Execute, () => !IsBusy);
        RevokeCommand = new RelayCommand(Revoke_Execute, () => !IsBusy);
        CopyOutputCommand = new RelayCommand(() => _ = CopyToClipboard(Output));
        ClearOutputCommand = new RelayCommand(() => Output = "");
        CloseTabCommand = new RelayCommand(() => _onClose(this));
    }

    private void CheckStatus_Execute() => Run("Checking certbot status...", p => _certbotService.CheckStatusAsync(p, default),
        result => result.Contains("certbot not found on this host", StringComparison.OrdinalIgnoreCase)
            ? ("Certbot is not installed on this host — use Install Certbot to add it.", "#f39c12")
            : ("Certbot is installed.", "#4caf50"));

    private void Install_Execute() => Run("Installing certbot...", p => _certbotService.InstallAsync(p, default),
        result =>
        {
            var sudoStatus = SudoAwareStatus(result, "");
            if (sudoStatus.Color != "#4caf50") return sudoStatus;
            if (result.Contains("No supported package manager found", StringComparison.OrdinalIgnoreCase))
                return ("No supported package manager found — install certbot manually.", "#f44336");
            if (result.Contains("already installed", StringComparison.OrdinalIgnoreCase))
                return ("Certbot was already installed.", "#4caf50");
            return ("Certbot installed.", "#4caf50");
        });

    private void ListCertificates_Execute() => Run("Listing certificates...", p => _certbotService.ListCertificatesAsync(p, default),
        result => SudoAwareStatus(result, "Certificates listed."));

    /// <summary>
    /// A command that ran fine over SSH (no exception) can still have failed on the remote
    /// side — most commonly sudo refusing the command (wrong/no sudo access, not in
    /// sudoers, no TTY). RunCommandAsync returns everything as plain text with no separate
    /// success flag, so this inspects well-known failure phrasing directly rather than
    /// defaulting to "success" whenever nothing else matched — that's what let a "not in
    /// the sudoers file" failure show up as a green "Certbot installed." before, and what
    /// would let a failed certbot challenge show up as a green "Dry run succeeded." too.
    /// </summary>
    private static (string Status, string Color) SudoAwareStatus(string result, string successMessage)
    {
        if (SudoFailureDetector.IsSudoFailure(result, out var sudoReason))
            return (sudoReason, "#f44336");

        if (result.Contains("Some challenges have failed", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Certbot failed to authenticate", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Certbot failed to renew", StringComparison.OrdinalIgnoreCase))
        {
            return ("Failed: certbot could not complete the request — see output below.", "#f44336");
        }

        // No blanket "any stderr present" check here — certbot writes
        // "Saving debug log to /var/log/letsencrypt/letsencrypt.log" to stderr on every
        // single invocation, success or failure, so that would flag every successful call
        // as a warning. Only the specific known-failure phrasing above should downgrade
        // the status; anything else genuinely is a success.
        return (successMessage, "#4caf50");
    }

    private void ObtainCertificate_Execute()
    {
        var domains = DomainsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var plugin = SelectedPlugin switch
        {
            "Webroot" => CertbotService.CertbotPlugin.Webroot,
            "Nginx" => CertbotService.CertbotPlugin.Nginx,
            "Apache" => CertbotService.CertbotPlugin.Apache,
            _ => CertbotService.CertbotPlugin.Standalone
        };
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = domains,
            Email = Email,
            Plugin = plugin,
            WebrootPath = WebrootPath,
            DryRun = DryRun
        };

        Run(DryRun ? "Requesting certificate (dry run)..." : "Requesting certificate...",
            p => _certbotService.ObtainAsync(p, request, default),
            result => SudoAwareStatus(result, DryRun ? "Dry run succeeded." : "Certificate obtained/renewed."));
    }

    private void RenewAll_Execute() => Run(DryRun ? "Renewing certificates (dry run)..." : "Renewing certificates...",
        p => _certbotService.RenewAllAsync(p, DryRun, default),
        result => SudoAwareStatus(result, DryRun ? "Dry run succeeded." : "Renewal complete."));

    private void Revoke_Execute() => Run($"Revoking '{CertNameToRevoke}'...",
        p => _certbotService.RevokeAsync(p, CertNameToRevoke, default),
        result => SudoAwareStatus(result, "Certificate revoked."));

    private void Run(string startStatus, Func<SshConnectionProfile, Task<string>> action,
        Func<string, (string Status, string Color)>? onSuccess = null)
    {
        if (IsBusy) return;
        var profile = SelectedProfile;
        if (profile == null)
        {
            Status = "Select a saved session first.";
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
                var result = await action(profile);
                var (status, color) = onSuccess?.Invoke(result) ?? ("Done.", "#4caf50");
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

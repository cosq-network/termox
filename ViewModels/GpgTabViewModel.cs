using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Termox.Services;

namespace Termox.ViewModels;

public class GpgTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private string _title = "GPG Key Manager";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    // Shared command support
    public ICommand DisconnectCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; private set; } = new RelayCommand(() => { });

    // Properties
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#4caf50";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public ObservableCollection<GpgKeyDisplay> PublicKeys { get; } = new();
    public ObservableCollection<GpgKeyDisplay> SecretKeys { get; } = new();

    private GpgKeyDisplay? _selectedPublicKey;
    public GpgKeyDisplay? SelectedPublicKey
    {
        get => _selectedPublicKey;
        set { _selectedPublicKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportKey)); }
    }

    private GpgKeyDisplay? _selectedSecretKey;
    public GpgKeyDisplay? SelectedSecretKey
    {
        get => _selectedSecretKey;
        set { _selectedSecretKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportSecretKey)); }
    }

    private string _importKeyData = "";
    public string ImportKeyData
    {
        get => _importKeyData;
        set { _importKeyData = value; OnPropertyChanged(); }
    }

    private string _exportedKeyData = "";
    public string ExportedKeyData
    {
        get => _exportedKeyData;
        set { _exportedKeyData = value; OnPropertyChanged(); }
    }

    private bool _isImportModalVisible;
    public bool IsImportModalVisible
    {
        get => _isImportModalVisible;
        set { _isImportModalVisible = value; OnPropertyChanged(); }
    }

    private bool _isExportModalVisible;
    public bool IsExportModalVisible
    {
        get => _isExportModalVisible;
        set { _isExportModalVisible = value; OnPropertyChanged(); }
    }

    public bool CanExportKey => SelectedPublicKey != null;
    public bool CanExportSecretKey => SelectedSecretKey != null;
    public bool CanDeleteKey => SelectedPublicKey != null;

    // Commands
    public ICommand RefreshKeysCommand { get; }
    public ICommand ExportPublicKeyCommand { get; }
    public ICommand ExportSecretKeyCommand { get; }
    public ICommand DeleteKeyCommand { get; }
    public ICommand ImportKeyCommand { get; }
    public ICommand ShowImportModalCommand { get; }
    public ICommand ShowExportModalCommand { get; }
    public ICommand CopyExportedKeyCommand { get; }
    public ICommand CloseImportModalCommand { get; }
    public ICommand CloseExportModalCommand { get; }

    private readonly GpgKeyManager _gpgManager = new();

    public GpgTabViewModel(Action<GpgTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => { onClose(this); });

        RefreshKeysCommand = new RelayCommand(RefreshKeys_Execute);
        ExportPublicKeyCommand = new RelayCommand(ExportPublicKey_Execute);
        ExportSecretKeyCommand = new RelayCommand(ExportSecretKey_Execute);
        DeleteKeyCommand = new RelayCommand(DeleteKey_Execute);
        ImportKeyCommand = new RelayCommand(ImportKey_Execute);
        ShowImportModalCommand = new RelayCommand(() => IsImportModalVisible = true);
        ShowExportModalCommand = new RelayCommand(() => IsExportModalVisible = true);
        CopyExportedKeyCommand = new RelayCommand(() => _ = CopyToClipboard(ExportedKeyData));
        CloseImportModalCommand = new RelayCommand(() => { IsImportModalVisible = false; ImportKeyData = ""; });
        CloseExportModalCommand = new RelayCommand(() => { IsExportModalVisible = false; ExportedKeyData = ""; });

        // Load keys on initialization
        RefreshKeys_Execute();
    }

    private void RefreshKeys_Execute()
    {
        IsLoading = true;
        Status = "Loading keys...";
        StatusColor = "#ffc107";

        Task.Run(async () =>
        {
            try
            {
                var publicKeys = await _gpgManager.ListPublicKeysAsync();
                var secretKeys = await _gpgManager.ListSecretKeysAsync();

                Dispatcher.UIThread.Post(() =>
                {
                    PublicKeys.Clear();
                    foreach (var key in publicKeys)
                    {
                        PublicKeys.Add(new GpgKeyDisplay(key));
                    }

                    SecretKeys.Clear();
                    foreach (var key in secretKeys)
                    {
                        SecretKeys.Add(new GpgKeyDisplay(key));
                    }

                    Status = $"Loaded {publicKeys.Count} public key(s) and {secretKeys.Count} secret key(s)";
                    StatusColor = "#4caf50";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Error: {ex.Message}";
                    StatusColor = "#f44336";
                    PublicKeys.Clear();
                    SecretKeys.Clear();
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        });
    }

    private void ExportPublicKey_Execute()
    {
        if (SelectedPublicKey == null) return;

        ExportedKeyData = "Exporting...";
        IsExportModalVisible = true;

        Task.Run(async () =>
        {
            try
            {
                var keyData = await _gpgManager.ExportPublicKeyAsync(SelectedPublicKey.KeyId);
                Dispatcher.UIThread.Post(() => ExportedKeyData = keyData);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ExportedKeyData = $"Export failed: {ex.Message}");
            }
        });
    }

    private void ExportSecretKey_Execute()
    {
        if (SelectedSecretKey == null) return;

        ExportedKeyData = "Exporting (this may require authentication)...";
        IsExportModalVisible = true;

        Task.Run(async () =>
        {
            try
            {
                var keyData = await _gpgManager.ExportSecretKeyAsync(SelectedSecretKey.KeyId);
                Dispatcher.UIThread.Post(() => ExportedKeyData = keyData);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ExportedKeyData = $"Export failed: {ex.Message}");
            }
        });
    }

    private void DeleteKey_Execute()
    {
        if (SelectedPublicKey == null) return;

        IsLoading = true;
        Task.Run(async () =>
        {
            try
            {
                var (success, message) = await _gpgManager.DeleteKeyAsync(SelectedPublicKey.KeyId);
                Dispatcher.UIThread.Post(() =>
                {
                    if (success)
                    {
                        Status = message;
                        StatusColor = "#4caf50";
                        RefreshKeys_Execute();
                    }
                    else
                    {
                        Status = $"Delete failed: {message}";
                        StatusColor = "#f44336";
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Error: {ex.Message}";
                    StatusColor = "#f44336";
                    IsLoading = false;
                });
            }
        });
    }

    private void ImportKey_Execute()
    {
        if (string.IsNullOrWhiteSpace(ImportKeyData))
        {
            Status = "Please paste key data";
            StatusColor = "#f44336";
            return;
        }

        IsLoading = true;
        Task.Run(async () =>
        {
            try
            {
                var (success, message) = await _gpgManager.ImportKeyAsync(ImportKeyData);
                Dispatcher.UIThread.Post(() =>
                {
                    Status = message;
                    StatusColor = success ? "#4caf50" : "#f44336";

                    if (success)
                    {
                        IsImportModalVisible = false;
                        ImportKeyData = "";
                        RefreshKeys_Execute();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Error: {ex.Message}";
                    StatusColor = "#f44336";
                    IsLoading = false;
                });
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

public class GpgKeyDisplay
{
    public string KeyId { get; set; }
    public string UserId { get; set; }
    public string Fingerprint { get; set; }
    public string CreatedDate { get; set; }
    public string ExpiryDate { get; set; }
    public bool IsExpired { get; set; }
    public string KeyType { get; set; }
    public string DisplayText { get; set; }

    public GpgKeyDisplay(GpgKeyManager.GpgKey key)
    {
        KeyId = key.KeyId;
        UserId = key.UserId;
        Fingerprint = key.Fingerprint;
        CreatedDate = key.CreatedDate.ToString("yyyy-MM-dd");
        ExpiryDate = key.ExpiryDate?.ToString("yyyy-MM-dd") ?? "Never";
        IsExpired = key.IsExpired;
        KeyType = key.KeyType;
        DisplayText = $"{UserId} ({KeyId}) - Created: {CreatedDate}";
    }
}

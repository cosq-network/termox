using AvaloniaEdit.Document;
using Renci.SshNet;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

public class FileEditorTabViewModel : INotifyPropertyChanged, ITabViewModel, IDisposable
{
    private readonly Action<FileEditorTabViewModel> _onClose;
    private readonly SshConnectionProfile _profile;
    private readonly RemoteTextFile _file;
    private readonly string _remotePath;
    private bool _disposed;
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private Encoding _fileEncoding = new System.Text.UTF8Encoding(false);
    private long _loadedLength = -1;
    private DateTime _loadedLastWriteTime;

    private string _title;
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public string RemotePath => _remotePath;

    private TextDocument _document = new();
    public TextDocument Document
    {
        get => _document;
        set
        {
            if (_document == value) return;
            if (_document != null) _document.TextChanged -= Document_TextChanged;
            _document = value;
            if (_document != null) _document.TextChanged += Document_TextChanged;
            OnPropertyChanged();
        }
    }

    private bool _loaded;
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(SaveButtonText)); }
    }

    public string SaveButtonText => IsDirty ? "Save*" : "Save";

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    private bool _isSaving;
    public bool IsSaving { get => _isSaving; set { _isSaving = value; OnPropertyChanged(); } }

    private string _status = "Loading...";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private string _statusColor = "#f39c12";
    public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }

    private bool _isSaveConfirmVisible;
    public bool IsSaveConfirmVisible { get => _isSaveConfirmVisible; set { _isSaveConfirmVisible = value; OnPropertyChanged(); } }

    public ICommand SaveCommand { get; }
    public ICommand SaveAndCloseCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ConfirmDiscardCommand { get; }
    public ICommand CancelDiscardCommand { get; }

    public FileEditorTabViewModel(Action<FileEditorTabViewModel> onClose, SshConnectionProfile profile, RemoteTextFile file)
    {
        _onClose = onClose;
        _profile = profile;
        _file = file;
        _remotePath = file.RemotePath;
        _title = $"Edit: {file.DisplayName}";

        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        SaveAndCloseCommand = new RelayCommand(() => _ = SaveAndCloseAsync());
        CloseTabCommand = new RelayCommand(RequestClose);
        DisconnectCommand = new RelayCommand(RequestClose);
        ConfirmDiscardCommand = new RelayCommand(CloseNow);
        CancelDiscardCommand = new RelayCommand(() => IsSaveConfirmVisible = false);

        _ = LoadAsync();
    }

    private void Document_TextChanged(object? sender, EventArgs e)
    {
        if (_loaded) IsDirty = true;
    }

    private async Task LoadAsync()
    {
        Status = "Loading...";
        StatusColor = "#f39c12";
        try
        {
            var loaded = await Task.Run(() =>
            {
                using var client = CreateClient();
                client.Connect();
                var attributes = client.GetAttributes(_file.RemotePath);
                using var stream = client.OpenRead(_file.RemotePath);
                var bytes = ReadLimited(stream);
                using var reader = new StreamReader(new MemoryStream(bytes), new System.Text.UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);
                if (LooksLikeBinary(bytes))
                    throw new InvalidDataException("The remote file appears to be binary and cannot be edited as text.");
                var content = reader.ReadToEnd();
                return (content, reader.CurrentEncoding, attributes.Size, attributes.LastWriteTime);
            });

            if (_disposed) return;

            _fileEncoding = loaded.Item2;
            _loadedLength = loaded.Item3;
            _loadedLastWriteTime = loaded.Item4;
            Document = new TextDocument(loaded.Item1);
            _loaded = true;
            IsDirty = false;
            IsLoading = false;
            Status = $"Loaded {_file.DisplayName}";
            StatusColor = "#4caf50";
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            IsLoading = false;
            Status = $"Failed to load: {ex.Message}";
            StatusColor = "#f44336";
        }
    }

    private async Task SaveAsync()
    {
        if (!IsDirty || IsSaving) return;
        IsSaving = true;
        Status = "Saving...";
        StatusColor = "#f39c12";
        try
        {
            var content = Document.Text;
            await Task.Run(() =>
            {
                using var client = CreateClient();
                client.Connect();
                var attributes = client.GetAttributes(_file.RemotePath);
                if ((_loadedLength >= 0 && attributes.Size != _loadedLength) ||
                    (_loadedLastWriteTime != default && attributes.LastWriteTime != _loadedLastWriteTime))
                    throw new InvalidOperationException("The remote file changed after it was loaded. Reload it before saving.");
                using var stream = client.OpenWrite(_file.RemotePath);
                using var writer = new StreamWriter(stream, _fileEncoding);
                writer.Write(content);
                writer.Flush();
            });

            if (_disposed) return;
            IsDirty = false;
            Status = $"Saved {_file.DisplayName}";
            StatusColor = "#4caf50";
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            Status = $"Save failed: {ex.Message}";
            StatusColor = "#f44336";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveAndCloseAsync()
    {
        if (!IsDirty)
        {
            CloseNow();
            return;
        }

        await SaveAsync();
        if (!IsDirty) CloseNow();
    }

    private void RequestClose()
    {
        if (IsDirty) IsSaveConfirmVisible = true;
        else CloseNow();
    }

    private void CloseNow()
    {
        _onClose(this);
    }

    private SftpClient CreateClient()
    {
        var decrypted = CredentialManager.DecryptProfile(_profile);
        var client = SshConnectionFactory.CreateSftpClient(decrypted);
        SshSecurity.ConfigureHostKeyPolicy(client, decrypted.HostKeyFingerprint, null);
        return client;
    }

    private static byte[] ReadLimited(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes)
                throw new InvalidDataException("The remote file is larger than the 20MB editor limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool LooksLikeBinary(byte[] data)
    {
        var sampleLength = Math.Min(data.Length, 8192);
        var hasUnicodeBom = sampleLength >= 2 &&
            ((data[0] == 0xFF && data[1] == 0xFE) ||
             (data[0] == 0xFE && data[1] == 0xFF)) ||
            sampleLength >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ||
            sampleLength >= 4 &&
            ((data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00) ||
             (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF));
        if (hasUnicodeBom) return false;

        for (var index = 0; index < sampleLength; index++)
        {
            var value = data[index];
            if (value == 0) return true;
            if (value < 0x09 || value is > 0x0D and < 0x20)
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_document != null) _document.TextChanged -= Document_TextChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}

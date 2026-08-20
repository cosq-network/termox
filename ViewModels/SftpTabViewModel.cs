using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

public class SftpTabViewModel : INotifyPropertyChanged, ITabViewModel, IDisposable
{
    private SftpClient? _sftpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;
    private readonly CancellationTokenSource _featureCancellation = new();
    private const long TextFileLimitBytes = 1024 * 1024;
    private readonly object _connectionLock = new();
    private CancellationTokenSource? _connectionCancellation;
    private ObservableCollection<RemoteFileModel> _allFiles = new();

    private string _title = "New SFTP Tab";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public string ConnectionHost { get; private set; } = "";
    public int ConnectionPort { get; private set; } = 22;
    public string ConnectionUsername { get; private set; } = "";
    public string? ConnectionProfileId { get; set; }

    private string _status = "Disconnected";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private string _statusColor = "#888888";
    public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }

    private string _currentPath = "/";
    public string CurrentPath { get => _currentPath; set { _currentPath = value; OnPropertyChanged(); } }

    public ObservableCollection<RemoteFileModel> Files { get; } = new();

    private RemoteFileModel? _selectedFile;
    public RemoteFileModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            OnPropertyChanged();
        }
    }

    private bool _canDownload;
    public bool CanDownload
    {
        get => _canDownload;
        set { _canDownload = value; OnPropertyChanged(); }
    }

    public void UpdateCanDownload(bool can) => CanDownload = can;

    public bool CanBulkDelete => Files.Any(file => file.IsSelected);

    private bool _isTransferring;
    public bool IsTransferring { get => _isTransferring; set { _isTransferring = value; OnPropertyChanged(); } }

    private volatile bool _isPaused;
    public bool IsPaused { get => _isPaused; set { _isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(PauseButtonText)); } }

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    private volatile bool _cancelRequested;

    private bool _isDeleteConfirmationVisible;
    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        set { _isDeleteConfirmationVisible = value; OnPropertyChanged(); }
    }

    private string _deleteConfirmationMessage = "Are you sure you want to delete the selected item?";
    public string DeleteConfirmationMessage
    {
        get => _deleteConfirmationMessage;
        set { _deleteConfirmationMessage = value; OnPropertyChanged(); }
    }

    private RemoteFileModel? _deleteTarget;
    private bool _deleteSelection;

    private bool _isFileSizeWarningVisible;
    public bool IsFileSizeWarningVisible
    {
        get => _isFileSizeWarningVisible;
        set { _isFileSizeWarningVisible = value; OnPropertyChanged(); }
    }

    private string _fileSizeWarningMessage = "";
    public string FileSizeWarningMessage
    {
        get => _fileSizeWarningMessage;
        set { _fileSizeWarningMessage = value; OnPropertyChanged(); }
    }

    private long _fileSizeWarningThreshold = 100 * 1024 * 1024;  // 100 MB default
    public long FileSizeWarningThreshold
    {
        get => _fileSizeWarningThreshold;
        set { _fileSizeWarningThreshold = value; OnPropertyChanged(); }
    }

    private List<RemoteFileModel>? _pendingDownloadFiles;
    private string _pendingDownloadFolder = "";

    private string _searchQuery = "";
    private System.Threading.Timer? _searchDebounceTimer;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            _searchQuery = value;
            OnPropertyChanged();
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = new System.Threading.Timer(
                _ => Dispatcher.UIThread.Post(ApplyFilter),
                null,
                TimeSpan.FromMilliseconds(200),
                Timeout.InfiniteTimeSpan);
        }
    }

    public ICommand TogglePauseCommand { get; }
    public ICommand CancelTransferCommand { get; }

    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand NavigateToCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DeleteFileCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand RenameFileCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand BulkDeleteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ConfirmFileSizeWarningCommand { get; }
    public ICommand CancelFileSizeWarningCommand { get; }

    public SftpTabViewModel(Action<SftpTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(() => _ = DisconnectAsync());
        CloseTabCommand = new RelayCommand(() => _ = CloseAsync(onClose));
        NavigateUpCommand = new RelayCommand(NavigateUp);
        NavigateToCommand = new RelayCommand<RemoteFileModel>(NavigateTo, f => f != null && f.IsDirectory);
        RefreshCommand = new RelayCommand(LoadDirectory);
        TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        CancelTransferCommand = new RelayCommand(() => _cancelRequested = true);
        DeleteFileCommand = new RelayCommand(RequestDeleteSelectedFile);
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteCommand = new RelayCommand(() => { IsDeleteConfirmationVisible = false; _deleteTarget = null; });
        RenameFileCommand = new RelayCommand(RenameSelectedFile);
        AddBookmarkCommand = new RelayCommand(() => NotifyAddBookmark?.Invoke(CurrentPath));
        BulkDeleteCommand = new RelayCommand(RequestBulkDelete);
        SelectAllCommand = new RelayCommand(SelectAllFiles);
        ClearSelectionCommand = new RelayCommand(ClearAllSelections);
        ConfirmFileSizeWarningCommand = new RelayCommand(ConfirmFileSizeWarning);
        CancelFileSizeWarningCommand = new RelayCommand(CancelFileSizeWarning);
    }

    public event Action<string>? NotifyAddBookmark;

    public void UpdateSelection(IEnumerable<RemoteFileModel> selectedFiles)
    {
        var selected = selectedFiles.ToHashSet();
        foreach (var file in Files)
            file.IsSelected = selected.Contains(file);

        OnPropertyChanged(nameof(CanBulkDelete));
    }

    public void Connect(string host, int port, string username, string password, string privateKeyPath,
        string? hostKeyFingerprint = null, Action<string>? firstSeenHostKey = null,
        string? initialPath = null)
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
        Title = $"SFTP: {host}";
        Status = "Connecting...";
        StatusColor = "#f39c12";

        Task.Run(() =>
        {
            _operationGate.Wait();
            SftpClient? client = null;
            var connected = false;
            try
            {
                if (cancellation.IsCancellationRequested) return;
                SshSecurity.EnsurePrivateKeyExists(privateKeyPath);
                var safeUsername = username ?? "";
                var safePassword = password ?? "";

                if (!string.IsNullOrWhiteSpace(privateKeyPath) && System.IO.File.Exists(privateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(privateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    client = new SftpClient(host, port, safeUsername, new[] { keyFile });
                }
                else
                {
                    client = new SftpClient(host, port, safeUsername, safePassword);
                }

                client.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;
                SshSecurity.ConfigureHostKeyPolicy(client, hostKeyFingerprint, firstSeenHostKey);
                lock (_connectionLock)
                {
                    if (cancellation.IsCancellationRequested) return;
                    _sftpClient = client;
                }

                client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
                connected = true;
                if (cancellation.IsCancellationRequested)
                {
                    _sftpClient.Disconnect();
                    return;
                }
                CurrentPath = string.IsNullOrWhiteSpace(initialPath)
                    ? _sftpClient.WorkingDirectory
                    : initialPath;

                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Connected to " + host;
                    StatusColor = "#4caf50";
                });

            }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested) return;
                lock (_connectionLock)
                {
                    if (ReferenceEquals(_sftpClient, client)) _sftpClient = null;
                }
                try { client?.Dispose(); } catch { }
                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Error: " + ex.Message;
                    StatusColor = "#f44336";
                });
                Console.WriteLine($"SFTP Connection Failed: {ex.Message}");
            }
            finally
            {
                if (client != null && !ReferenceEquals(client, _sftpClient)) client.Dispose();
                _operationGate.Release();
                if (connected && !cancellation.IsCancellationRequested)
                    Dispatcher.UIThread.Post(LoadDirectory);
            }
        });
    }

    public Task DisconnectAsync()
    {
        lock (_connectionLock) _connectionCancellation?.Cancel();
        _cancelRequested = true;
        IsPaused = false;
        return Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                SftpClient? client;
                lock (_connectionLock)
                {
                    client = _sftpClient;
                    _sftpClient = null;
                }
                client?.Disconnect();
                client?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SFTP Disconnect Error: {ex.Message}");
            }
            finally
            {
                _operationGate.Release();
                Dispatcher.UIThread.Post(() =>
                {
                    Status = "Disconnected";
                    StatusColor = "#888888";
                    Files.Clear();
                    _allFiles.Clear();
                });
            }
        });
    }

    private void LoadDirectory()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
            if (!_operationGate.Wait(0)) return;
            try
            {
                PostDirectoryListing(_sftpClient.ListDirectory(CurrentPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SFTP List Directory Error: {ex.Message}");
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    private void ApplyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Files.Clear();
            var filtered = string.IsNullOrWhiteSpace(SearchQuery)
                ? _allFiles
                : _allFiles.Where(f => f.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

            foreach (var file in filtered)
            {
                Files.Add(file);
            }

            OnPropertyChanged(nameof(CanBulkDelete));
        });
    }

    private void NavigateUp()
    {
        if (CurrentPath == "/") return;
        var lastSlash = CurrentPath.LastIndexOf('/');
        if (lastSlash <= 0) CurrentPath = "/";
        else CurrentPath = CurrentPath.Substring(0, lastSlash);
        LoadDirectory();
    }

    private void NavigateTo(RemoteFileModel? file)
    {
        if (file == null || !file.IsDirectory) return;
        CurrentPath = file.FullName;
        LoadDirectory();
    }

    public void NavigateToPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = NormalizeRemotePath(path);
        if (normalized == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = $"Invalid path: '{path}'. Use an absolute path such as /home/user.";
                StatusColor = "#f44336";
            });
            return;
        }

        var target = normalized;
        Task.Run(() =>
        {
            if (!_operationGate.Wait(0)) return;
            try
            {
                if (_sftpClient == null || !_sftpClient.IsConnected)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = "Not connected.";
                        StatusColor = "#f44336";
                    });
                    return;
                }

                var attributes = _sftpClient.GetAttributes(target);
                if (!attributes.IsDirectory)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = $"'{target}' is not a directory.";
                        StatusColor = "#f39c12";
                    });
                    return;
                }

                var files = _sftpClient.ListDirectory(target);
                Dispatcher.UIThread.Post(() =>
                {
                    CurrentPath = target;
                    PostDirectoryListing(files);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Cannot navigate: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    private void PostDirectoryListing(IEnumerable<ISftpFile> files)
    {
        var entries = files
            .Where(file => file.Name != "." && file.Name != "..")
            .OrderByDescending(file => file.IsDirectory)
            .ThenBy(file => file.Name)
            .Select(file => new RemoteFileModel
            {
                Name = file.Name,
                FullName = file.FullName,
                IsDirectory = file.IsDirectory,
                IsSymbolicLink = file.IsSymbolicLink,
                Length = file.Length,
                LastWriteTime = file.LastWriteTime,
                Permissions = GetPermissionsString(file)
            })
            .ToList();

        Dispatcher.UIThread.Post(() =>
        {
            _allFiles.Clear();
            foreach (var entry in entries)
                _allFiles.Add(entry);
            ApplyFilter();
        });
    }

    private static string? NormalizeRemotePath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0) return null;

        var normalized = trimmed.Replace('\\', '/');
        if (normalized[0] != '/') return null;

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) continue;
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        var result = "/" + string.Join("/", segments);
        return result.Length == 1 ? result : result.TrimEnd('/');
    }

    private string GetPermissionsString(ISftpFile file)
    {
        char[] p = new char[10];
        p[0] = file.IsDirectory ? 'd' : (file.IsSymbolicLink ? 'l' : '-');
        p[1] = file.OwnerCanRead ? 'r' : '-';
        p[2] = file.OwnerCanWrite ? 'w' : '-';
        p[3] = file.OwnerCanExecute ? 'x' : '-';
        p[4] = file.GroupCanRead ? 'r' : '-';
        p[5] = file.GroupCanWrite ? 'w' : '-';
        p[6] = file.GroupCanExecute ? 'x' : '-';
        p[7] = file.OthersCanRead ? 'r' : '-';
        p[8] = file.OthersCanWrite ? 'w' : '-';
        p[9] = file.OthersCanExecute ? 'x' : '-';
        return new string(p);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }

    public async Task UploadFilesAsync(System.Collections.Generic.IEnumerable<string> localFilePaths)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var paths = localFilePaths.ToList();
        if (paths.Count == 0) return;

        IsTransferring = true;
        _cancelRequested = false;
        IsPaused = false;

        await _operationGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var localFilePath in paths)
                    {
                        if (_cancelRequested) break;

                        var fileName = System.IO.Path.GetFileName(localFilePath);
                        var remoteFilePath = CurrentPath == "/" ? $"/{fileName}" : $"{CurrentPath}/{fileName}";
                        Dispatcher.UIThread.Post(() => Status = $"Uploading {fileName}...");

                        using var fileStream = System.IO.File.OpenRead(localFilePath);
                        using var sftpStream = _sftpClient.OpenWrite(remoteFilePath);
                        var buffer = new byte[81920];
                        int read;
                        ulong totalUploaded = 0;
                        DateTime lastUpdate = DateTime.MinValue;

                        while ((read = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (_cancelRequested) break;
                            while (IsPaused && !_cancelRequested) System.Threading.Thread.Sleep(100);
                            if (_cancelRequested) break;

                            sftpStream.Write(buffer, 0, read);
                            totalUploaded += (ulong)read;
                            if ((DateTime.Now - lastUpdate).TotalMilliseconds > 250)
                            {
                                lastUpdate = DateTime.Now;
                                var uploaded = totalUploaded;
                                Dispatcher.UIThread.Post(() => Status = $"Uploading {fileName}... {FormatSize((long)uploaded)}");
                            }
                        }

                        if (_cancelRequested)
                        {
                            try { _sftpClient.DeleteFile(remoteFilePath); } catch { }
                        }
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = _cancelRequested ? "Upload Cancelled." : $"Uploaded {paths.Count} items successfully!";
                        StatusColor = _cancelRequested ? "#f44336" : "#4caf50";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => { Status = $"Upload Failed: {ex.Message}"; StatusColor = "#f44336"; });
                }
                finally
                {
                    Dispatcher.UIThread.Post(() => { IsTransferring = false; _cancelRequested = false; IsPaused = false; });
                }
            });
        }
        finally { _operationGate.Release(); }
        LoadDirectory();
    }

    /// <summary>
    /// Starts a download, optionally prompting the user first when the total size
    /// of the selected items exceeds the configured warning threshold.
    /// </summary>
    public async Task DownloadFilesAsync(System.Collections.IList filesToDownload, string localFolderPath,
        bool skipSizeWarning = false)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var files = filesToDownload.Cast<RemoteFileModel>().ToList();
        if (files.Count == 0) return;

        if (!skipSizeWarning && IsFileSizeWarningVisible)
            return;  // A confirmation is already on screen; ignore repeated triggers.

        // Prompt before starting the transfer when the selection is large.
        if (!skipSizeWarning && FileSizeWarningThreshold > 0)
        {
            var totalSize = files
                .Where(f => !f.IsDirectory)
                .Sum(f => Math.Max(f.Length, 0L));
            var largeItems = files.Where(f => !f.IsDirectory && f.Length > FileSizeWarningThreshold).ToList();

            if (largeItems.Count > 0 || totalSize > FileSizeWarningThreshold)
            {
                _pendingDownloadFiles = files;
                _pendingDownloadFolder = localFolderPath;
                var details = string.Join(", ",
                    largeItems.OrderByDescending(f => f.Length)
                        .Take(3)
                        .Select(f => $"{f.Name} ({FormatSize(f.Length)})"));
                FileSizeWarningMessage =
                    $"The selection contains {FormatSize(totalSize)} of data. " +
                    (largeItems.Count > 0
                        ? $"Largest item(s): {details}. "
                        : "") +
                    "Continue with the download?";
                IsFileSizeWarningVisible = true;
                return;
            }
        }

        await DownloadFilesCoreAsync(files, localFolderPath);
    }

    private void ConfirmFileSizeWarning()
    {
        IsFileSizeWarningVisible = false;
        var files = _pendingDownloadFiles;
        var folder = _pendingDownloadFolder;
        _pendingDownloadFiles = null;
        _pendingDownloadFolder = "";
        if (files != null && files.Count > 0)
            _ = DownloadFilesCoreAsync(files, folder);
    }

    private void CancelFileSizeWarning()
    {
        IsFileSizeWarningVisible = false;
        _pendingDownloadFiles = null;
        _pendingDownloadFolder = "";
    }

    private async Task DownloadFilesCoreAsync(List<RemoteFileModel> files, string localFolderPath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;
        if (files.Count == 0) return;

        IsTransferring = true;
        _cancelRequested = false;
        IsPaused = false;

        Status = $"Downloading {files.Count} items...";
        StatusColor = "#f39c12";

        var rootPath = System.IO.Path.GetFullPath(localFolderPath);
        System.IO.Directory.CreateDirectory(rootPath);
        await _operationGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var file in files)
                    {
                        if (_cancelRequested) break;
                        var localFilePath = EnsureSafeLocalPath(rootPath, System.IO.Path.Combine(rootPath, file.Name));
                        if (file.IsDirectory && !file.IsSymbolicLink)
                        {
                            System.IO.Directory.CreateDirectory(localFilePath);
                            DownloadDirectoryRecursively(file.FullName, localFilePath, rootPath);
                        }
                        else DownloadSingleFile(file.FullName, localFilePath, file.Name);
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = _cancelRequested ? "Download Cancelled." : $"Downloaded {files.Count} items successfully!";
                        StatusColor = _cancelRequested ? "#f44336" : "#4caf50";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => { Status = $"Download Failed: {ex.Message}"; StatusColor = "#f44336"; });
                }
                finally
                {
                    Dispatcher.UIThread.Post(() => { IsTransferring = false; _cancelRequested = false; IsPaused = false; });
                }
            });
        }
        finally { _operationGate.Release(); }
        LoadDirectory();
    }

    private void DownloadDirectoryRecursively(string remotePath, string localPath, string rootPath)
    {
        if (_cancelRequested) return;

        var files = _sftpClient!.ListDirectory(remotePath);
        foreach (var file in files)
        {
            if (_cancelRequested) break;
            if (file.Name == "." || file.Name == "..") continue;

            string localFilePath = EnsureSafeLocalPath(rootPath, System.IO.Path.Combine(localPath, file.Name));
            if (file.IsDirectory && !file.IsSymbolicLink)
            {
                System.IO.Directory.CreateDirectory(localFilePath);
                DownloadDirectoryRecursively(file.FullName, localFilePath, rootPath);
            }
            else
            {
                DownloadSingleFile(file.FullName, localFilePath, file.Name);
            }
        }
    }

    private void DownloadSingleFile(string remoteFilePath, string localFilePath, string displayFileName)
    {
        if (System.IO.File.Exists(localFilePath))
        {
            Console.WriteLine($"Skipping '{displayFileName}' — already exists locally.");
            return;
        }

        using var sftpStream = _sftpClient!.OpenRead(remoteFilePath);
        using var fileStream = new FileStream(localFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        ulong totalRead = 0;
        DateTime lastUpdate = DateTime.MinValue;

        while ((read = sftpStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (_cancelRequested) break;

            while (IsPaused)
            {
                if (_cancelRequested) break;
                System.Threading.Thread.Sleep(100);
            }

            if (_cancelRequested) break;

            fileStream.Write(buffer, 0, read);
            totalRead += (ulong)read;

            if ((DateTime.Now - lastUpdate).TotalMilliseconds > 250)
            {
                lastUpdate = DateTime.Now;
                Dispatcher.UIThread.Post(() => {
                    Status = $"Downloading {displayFileName}... {FormatSize((long)totalRead)}";
                });
            }
        }

        fileStream.Close();
        if (_cancelRequested && System.IO.File.Exists(localFilePath))
        {
            try { System.IO.File.Delete(localFilePath); } catch { }
        }
    }

    private static string EnsureSafeLocalPath(string rootPath, string candidatePath)
    {
        return LocalPathSafety.EnsureWithinRoot(rootPath, candidatePath);
    }

    private void RequestDeleteSelectedFile()
    {
        if (_selectedFile == null || _sftpClient == null || !_sftpClient.IsConnected) return;
        _deleteTarget = _selectedFile;
        _deleteSelection = false;
        DeleteConfirmationMessage = $"Delete '{_selectedFile.Name}' from the remote server?";
        IsDeleteConfirmationVisible = true;
    }

    private void RequestBulkDelete()
    {
        var selectedFiles = Files.Where(f => f.IsSelected).ToList();
        if (_sftpClient == null || !_sftpClient.IsConnected || selectedFiles.Count == 0) return;
        _deleteTarget = null;
        _deleteSelection = true;
        DeleteConfirmationMessage = $"Delete {selectedFiles.Count} selected item(s) from the remote server? This cannot be undone.";
        IsDeleteConfirmationVisible = true;
    }

    private void ConfirmDelete()
    {
        IsDeleteConfirmationVisible = false;
        if (_deleteSelection)
        {
            BulkDeleteFiles();
        }
        else if (_deleteTarget != null)
        {
            DeleteSelectedFile(_deleteTarget);
        }
        _deleteTarget = null;
    }

    private void BulkDeleteFiles()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected || Files.Count == 0) return;

        var filesToDelete = Files.Where(f => f.IsSelected).ToList();
        if (filesToDelete.Count == 0) return;

        Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                int successCount = 0;
                int failureCount = 0;

                foreach (var file in filesToDelete)
                {
                    if (file.IsDirectory && !file.IsSymbolicLink)
                    {
                        try
                        {
                            DeleteDirectoryRecursively(file.FullName);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete directory {file.Name}: {ex.Message}");
                            failureCount++;
                        }
                    }
                    else
                    {
                        try
                        {
                            _sftpClient.DeleteFile(file.FullName);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete file {file.Name}: {ex.Message}");
                            failureCount++;
                        }
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Bulk delete: {successCount} deleted, {failureCount} failed.";
                    StatusColor = failureCount > 0 ? "#f39c12" : "#4caf50";
                    LoadDirectory();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Bulk delete failed: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    private void SelectAllFiles()
    {
        // This will be handled via UI binding, but provided for command support
        foreach (var file in Files)
        {
            if (file != null) file.IsSelected = true;
        }
        OnPropertyChanged(nameof(CanBulkDelete));
    }

    private void ClearAllSelections()
    {
        foreach (var file in Files)
        {
            if (file != null) file.IsSelected = false;
        }
        OnPropertyChanged(nameof(CanBulkDelete));
    }

    private static string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes <= 0) return "0 B";
        int place = Math.Min(Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024))), suf.Length - 1);
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{num} {suf[place]}";
    }

    public void ChangeFilePermissions(RemoteFileModel file, short newMode, MainViewModel mainVm)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                _sftpClient.ChangePermissions(file.FullName, newMode);
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Permissions changed for '{file.Name}' to {Convert.ToString(newMode, 8)}";
                    StatusColor = "#4caf50";
                    LoadDirectory();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Failed to change permissions: {ex.Message}";
                    StatusColor = "#f44336";
                });
                Console.WriteLine($"SFTP Permission Change Error: {ex.Message}");
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    private static readonly string[] TextExtensions = new[]
    {
        ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".cs", ".py", ".js", ".html",
        ".css", ".log", ".conf", ".cfg", ".properties", ".sh", ".bat", ".cmd"
    };

    private SshConnectionProfile? _connectionProfile;
    public SshConnectionProfile? ConnectionProfile
    {
        get => _connectionProfile;
        set { _connectionProfile = value; OnPropertyChanged(); }
    }

    private async Task CloseAsync(Action<SftpTabViewModel> onClose)
    {
        await DisconnectAsync();
        Dispose();
        onClose(this);
    }

    public async void PreviewFile(RemoteFileModel file, MainViewModel mainVm)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var acquired = false;
        try
        {
            await _operationGate.WaitAsync(_featureCancellation.Token);
            acquired = true;
            var ext = System.IO.Path.GetExtension(file.Name).ToLower();

            if (!TextExtensions.Contains(ext) || file.Length > TextFileLimitBytes)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    mainVm.FilePreviewContent = "Cannot preview this file type or file is too large (>1MB)";
                    mainVm.FilePreviewName = file.Name;
                    mainVm.IsFilePreviewModalVisible = true;
                });
                return;
            }

            using var stream = _sftpClient.OpenRead(file.FullName);
            var content = ReadTextWithLimit(stream);

            if (_disposed) return;

            Dispatcher.UIThread.Post(() =>
            {
                mainVm.FilePreviewContent = content;
                mainVm.FilePreviewName = file.Name;
                mainVm.IsFilePreviewModalVisible = true;
            });
        }
        catch (Exception ex)
        {
            if (_featureCancellation.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                mainVm.FilePreviewContent = $"Error reading file: {ex.Message}";
                mainVm.FilePreviewName = file.Name;
                mainVm.IsFilePreviewModalVisible = true;
            });
        }
        finally
        {
            if (acquired) _operationGate.Release();
        }
    }

    public async void OpenFileInEditor(RemoteFileModel file, MainViewModel mainVm)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var acquired = false;
        try
        {
            await _operationGate.WaitAsync(_featureCancellation.Token);
            acquired = true;
            var ext = System.IO.Path.GetExtension(file.Name).ToLower();

            if (file.IsDirectory || !TextExtensions.Contains(ext) || file.Length > TextFileLimitBytes)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    mainVm.EditorStatusMessage = "Cannot edit this file type or file is too large (>1MB)";
                });
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                mainVm.OpenFileEditorTab(this, new RemoteTextFile
                {
                    Host = ConnectionHost,
                    RemotePath = file.FullName,
                    DisplayName = file.Name,
                    Length = file.Length,
                    LastWriteTime = file.LastWriteTime
                });
            });
        }
        catch (Exception ex)
        {
            if (_featureCancellation.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                mainVm.EditorStatusMessage = $"Error opening file: {ex.Message}";
            });
        }
        finally
        {
            if (acquired) _operationGate.Release();
        }
    }

    private static string ReadTextWithLimit(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > TextFileLimitBytes)
                throw new InvalidDataException("The remote file is larger than the 1MB preview limit.");
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, new System.Text.UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void DeleteSelectedFile(RemoteFileModel selectedFile)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                if (selectedFile.IsDirectory && !selectedFile.IsSymbolicLink)
                {
                    DeleteDirectoryRecursively(selectedFile.FullName);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = $"Deleted directory '{selectedFile.Name}' successfully.";
                        StatusColor = "#4caf50";
                        LoadDirectory();
                    });
                }
                else
                {
                    _sftpClient.DeleteFile(selectedFile.FullName);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = $"Deleted file '{selectedFile.Name}' successfully.";
                        StatusColor = "#4caf50";
                        LoadDirectory();
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Delete Failed: {ex.Message}";
                    StatusColor = "#f44336";
                });
                Console.WriteLine($"SFTP Delete Error: {ex.Message}");
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    private void DeleteDirectoryRecursively(string remotePath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var files = _sftpClient.ListDirectory(remotePath);
        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..") continue;

            if (file.IsDirectory && !file.IsSymbolicLink)
            {
                DeleteDirectoryRecursively(file.FullName);
            }
            else
            {
                _sftpClient.DeleteFile(file.FullName);
            }
        }

        _sftpClient.DeleteDirectory(remotePath);
    }

    private string? _renameNewName;

    public void SetRenameNewName(string newName)
    {
        _renameNewName = newName;
    }

    private void RenameSelectedFile()
    {
        RemoteFileModel? fileToRename;
        lock (_connectionLock)
        {
            fileToRename = _selectedFile;
            if (fileToRename == null || _sftpClient == null || !_sftpClient.IsConnected) return;
        }

        var newName = _renameNewName;
        if (string.IsNullOrWhiteSpace(newName))
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = "Rename cancelled.";
                StatusColor = "#f39c12";
            });
            return;
        }

        if (newName.Contains('/') || newName.Contains('\\') ||
            newName is "." or ".." || newName.Contains("..", StringComparison.Ordinal))
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = "Rename failed: use a simple file or directory name.";
                StatusColor = "#f44336";
            });
            _renameNewName = null;
            return;
        }

        Task.Run(() =>
        {
            _operationGate.Wait();
            try
            {
                int lastSlash = fileToRename.FullName.LastIndexOf('/');
                string newFullPath = lastSlash >= 0
                    ? fileToRename.FullName.Substring(0, lastSlash + 1) + newName
                    : newName;
                _sftpClient.RenameFile(fileToRename.FullName, newFullPath);

                var name = fileToRename.Name;
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Renamed '{name}' to '{newName}' successfully.";
                    StatusColor = "#4caf50";
                    _renameNewName = null;
                    LoadDirectory();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Rename Failed: {ex.Message}";
                    StatusColor = "#f44336";
                    _renameNewName = null;
                });
                Console.WriteLine($"SFTP Rename Error: {ex.Message}");
            }
            finally
            {
                _operationGate.Release();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _featureCancellation.Cancel();
        _featureCancellation.Dispose();
        _searchDebounceTimer?.Dispose();
        _operationGate.Dispose();
    }
}

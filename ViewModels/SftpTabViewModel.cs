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

public class SftpTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private SftpClient? _sftpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
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

    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); ApplyFilter(); }
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

    public SftpTabViewModel(Action<SftpTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(Disconnect);
        CloseTabCommand = new RelayCommand(() => { Disconnect(); onClose(this); });
        NavigateUpCommand = new RelayCommand(NavigateUp);
        NavigateToCommand = new RelayCommand<RemoteFileModel>(NavigateTo!);
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
    }

    public event Action<string>? NotifyAddBookmark;

    public void UpdateSelection(IEnumerable<RemoteFileModel> selectedFiles)
    {
        var selected = selectedFiles.ToHashSet();
        foreach (var file in Files)
            file.IsSelected = selected.Contains(file);
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

                SshSecurity.ConfigureHostKeyPolicy(client, hostKeyFingerprint, firstSeenHostKey);
                lock (_connectionLock)
                {
                    if (cancellation.IsCancellationRequested) return;
                    _sftpClient = client;
                }

                _sftpClient.Connect();
                connected = true;
                if (cancellation.IsCancellationRequested)
                {
                    _sftpClient.Disconnect();
                    return;
                }
                CurrentPath = _sftpClient.WorkingDirectory;

                Status = "Connected to " + host;
                StatusColor = "#4caf50";

            }
            catch (Exception ex)
            {
                lock (_connectionLock)
                {
                    if (ReferenceEquals(_sftpClient, client)) _sftpClient = null;
                }
                try { client?.Dispose(); } catch { }
                Status = "Error: " + ex.Message;
                StatusColor = "#f44336";
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

    private void Disconnect()
    {
        lock (_connectionLock) _connectionCancellation?.Cancel();
        Task.Run(() =>
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
                var files = _sftpClient.ListDirectory(CurrentPath);
                Dispatcher.UIThread.Post(() =>
                {
                    _allFiles.Clear();
                    foreach (var file in files.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name))
                    {
                        if (file.Name == "." || file.Name == "..") continue;

                        _allFiles.Add(new RemoteFileModel
                        {
                            Name = file.Name,
                            FullName = file.FullName,
                            IsDirectory = file.IsDirectory,
                            IsSymbolicLink = file.IsSymbolicLink,
                            Length = file.Length,
                            LastWriteTime = file.LastWriteTime,
                            Permissions = GetPermissionsString(file)
                        });
                    }
                    ApplyFilter();
                });
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

    private void NavigateTo(RemoteFileModel file)
    {
        if (file == null || !file.IsDirectory) return;
        CurrentPath = file.FullName;
        LoadDirectory();
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

    public async Task DownloadFilesAsync(System.Collections.IList filesToDownload, string localFolderPath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        var files = filesToDownload.Cast<RemoteFileModel>().ToList();
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
            throw new IOException($"Download stopped because '{displayFileName}' already exists locally.");

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
        });
    }

    private void SelectAllFiles()
    {
        // This will be handled via UI binding, but provided for command support
        foreach (var file in Files)
        {
            if (file != null) file.IsSelected = true;
        }
    }

    private void ClearAllSelections()
    {
        foreach (var file in Files)
        {
            if (file != null) file.IsSelected = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        long bytesAbsolute = bytes == long.MinValue ? long.MaxValue : Math.Abs(bytes);
        int place = Math.Min(Convert.ToInt32(Math.Floor(Math.Log(bytesAbsolute, 1024))), suf.Length - 1);
        double num = Math.Round(bytesAbsolute / Math.Pow(1024, place), 1);
        return $"{Math.Sign(bytes) * num} {suf[place]}";
    }

    public void ChangeFilePermissions(RemoteFileModel file, short newMode, MainViewModel mainVm)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
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
        });
    }

    public void PreviewFile(RemoteFileModel file, MainViewModel mainVm)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        try
        {
            // Only preview text files
            var ext = System.IO.Path.GetExtension(file.Name).ToLower();
            var textExtensions = new[] { ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".cs", ".py", ".js", ".html", ".css", ".log", ".conf", ".cfg", ".properties", ".sh", ".bat", ".cmd", ".py" };

            if (!textExtensions.Contains(ext) || file.Length > 1024 * 1024) // Max 1MB
            {
                Dispatcher.UIThread.Post(() =>
                {
                    mainVm.FilePreviewContent = "Cannot preview this file type or file is too large (>1MB)";
                    mainVm.FilePreviewName = file.Name;
                });
                return;
            }

            using var stream = _sftpClient.OpenRead(file.FullName);
            using var reader = new System.IO.StreamReader(stream);
            var content = reader.ReadToEnd();

            Dispatcher.UIThread.Post(() =>
            {
                mainVm.FilePreviewContent = content;
                mainVm.FilePreviewName = file.Name;
                mainVm.IsFilePreviewModalVisible = true;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                mainVm.FilePreviewContent = $"Error reading file: {ex.Message}";
                mainVm.FilePreviewName = file.Name;
            });
        }
    }

    private void DeleteSelectedFile(RemoteFileModel selectedFile)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
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
        if (_selectedFile == null || _sftpClient == null || !_sftpClient.IsConnected) return;

        if (string.IsNullOrWhiteSpace(_renameNewName))
        {
            Status = "Rename cancelled.";
            StatusColor = "#f39c12";
            return;
        }

        if (_renameNewName.Contains('/') || _renameNewName.Contains('\\') ||
            _renameNewName is "." or ".." || _renameNewName.Contains("..", StringComparison.Ordinal))
        {
            Status = "Rename failed: use a simple file or directory name.";
            StatusColor = "#f44336";
            _renameNewName = null;
            return;
        }

        Task.Run(() =>
        {
            try
            {
                int lastSlash = _selectedFile.FullName.LastIndexOf('/');
                string newFullPath = lastSlash >= 0
                    ? _selectedFile.FullName.Substring(0, lastSlash + 1) + _renameNewName
                    : _renameNewName;
                _sftpClient.RenameFile(_selectedFile.FullName, newFullPath);

                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Renamed '{_selectedFile.Name}' to '{_renameNewName}' successfully.";
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
        });
    }
}

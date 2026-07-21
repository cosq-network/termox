using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;

namespace Termox.ViewModels;

public class SftpTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private SftpClient? _sftpClient;

    private string _title = "New SFTP Tab";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

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
            OnPropertyChanged(nameof(CanDownload));
        } 
    }

    public bool CanDownload => SelectedFile != null;

    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand NavigateToCommand { get; }
    public ICommand RefreshCommand { get; }

    public SftpTabViewModel(Action<SftpTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(Disconnect);
        CloseTabCommand = new RelayCommand(() => { Disconnect(); onClose(this); });
        NavigateUpCommand = new RelayCommand(NavigateUp);
        NavigateToCommand = new RelayCommand<RemoteFileModel>(NavigateTo!);
        RefreshCommand = new RelayCommand(LoadDirectory);
    }

    public void Connect(string host, int port, string username, string password, string privateKeyPath)
    {
        Title = $"SFTP: {host}";
        Status = "Connecting...";
        StatusColor = "#f39c12";

        Task.Run(() =>
        {
            try
            {
                var safeUsername = username ?? "";
                var safePassword = password ?? "";
                
                if (!string.IsNullOrWhiteSpace(privateKeyPath) && System.IO.File.Exists(privateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(privateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    _sftpClient = new SftpClient(host, port, safeUsername, new[] { keyFile });
                }
                else
                {
                    _sftpClient = new SftpClient(host, port, safeUsername, safePassword);
                }
                
                _sftpClient.Connect();
                CurrentPath = _sftpClient.WorkingDirectory;

                Status = "Connected to " + host;
                StatusColor = "#4caf50";
                
                LoadDirectory();
            }
            catch (Exception ex)
            {
                Status = "Error: " + ex.Message;
                StatusColor = "#f44336";
                Console.WriteLine($"SFTP Connection Failed: {ex.Message}");
            }
        });
    }

    private void Disconnect()
    {
        try
        {
            _sftpClient?.Disconnect();
            _sftpClient?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SFTP Disconnect Error: {ex.Message}");
        }
        finally
        {
            _sftpClient = null;
            Status = "Disconnected";
            StatusColor = "#888888";
            Dispatcher.UIThread.Post(Files.Clear);
        }
    }

    private void LoadDirectory()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        Task.Run(() =>
        {
            try
            {
                var files = _sftpClient.ListDirectory(CurrentPath);
                Dispatcher.UIThread.Post(() =>
                {
                    Files.Clear();
                    foreach (var file in files.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name))
                    {
                        if (file.Name == "." || file.Name == "..") continue;
                        
                        Files.Add(new RemoteFileModel
                        {
                            Name = file.Name,
                            FullName = file.FullName,
                            IsDirectory = file.IsDirectory,
                            Length = file.Length,
                            LastWriteTime = file.LastWriteTime,
                            Permissions = GetPermissionsString(file)
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SFTP List Directory Error: {ex.Message}");
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async Task UploadFileAsync(string localFilePath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        string fileName = System.IO.Path.GetFileName(localFilePath);
        string remoteFilePath = CurrentPath == "/" ? $"/{fileName}" : $"{CurrentPath}/{fileName}";
        
        Status = $"Uploading {fileName}...";
        StatusColor = "#f39c12";
        
        await Task.Run(() =>
        {
            try
            {
                using var fileStream = System.IO.File.OpenRead(localFilePath);
                DateTime lastUpdate = DateTime.MinValue;
                _sftpClient.UploadFile(fileStream, remoteFilePath, (uploadedBytes) => 
                {
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 250)
                    {
                        lastUpdate = DateTime.Now;
                        Dispatcher.UIThread.Post(() => {
                            Status = $"Uploading {fileName}... {FormatSize((long)uploadedBytes)}";
                        });
                    }
                });
                
                Dispatcher.UIThread.Post(() => {
                    Status = $"Uploaded {fileName} successfully!";
                    StatusColor = "#4caf50";
                    LoadDirectory();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    Status = $"Upload Failed: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
        });
    }

    public async Task DownloadFileAsync(string remoteFilePath, string localFolderPath, bool isDirectory)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected) return;

        string fileName = remoteFilePath.Substring(remoteFilePath.LastIndexOf('/') + 1);
        string localFilePath = System.IO.Path.Combine(localFolderPath, fileName);

        Status = $"Downloading {fileName}...";
        StatusColor = "#f39c12";
        
        await Task.Run(() =>
        {
            try
            {
                if (isDirectory)
                {
                    System.IO.Directory.CreateDirectory(localFilePath);
                    DownloadDirectoryRecursively(remoteFilePath, localFilePath);
                }
                else
                {
                    DownloadSingleFile(remoteFilePath, localFilePath, fileName);
                }
                
                Dispatcher.UIThread.Post(() => {
                    Status = $"Downloaded {fileName} successfully!";
                    StatusColor = "#4caf50";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    Status = $"Download Failed: {ex.Message}";
                    StatusColor = "#f44336";
                });
            }
        });
    }

    private void DownloadDirectoryRecursively(string remotePath, string localPath)
    {
        var files = _sftpClient!.ListDirectory(remotePath);
        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..") continue;
            
            string localFilePath = System.IO.Path.Combine(localPath, file.Name);
            if (file.IsDirectory)
            {
                System.IO.Directory.CreateDirectory(localFilePath);
                DownloadDirectoryRecursively(file.FullName, localFilePath);
            }
            else
            {
                DownloadSingleFile(file.FullName, localFilePath, file.Name);
            }
        }
    }

    private void DownloadSingleFile(string remoteFilePath, string localFilePath, string displayFileName)
    {
        using var fileStream = System.IO.File.Create(localFilePath);
        DateTime lastUpdate = DateTime.MinValue;
        _sftpClient!.DownloadFile(remoteFilePath, fileStream, (downloadedBytes) =>
        {
            if ((DateTime.Now - lastUpdate).TotalMilliseconds > 250)
            {
                lastUpdate = DateTime.Now;
                Dispatcher.UIThread.Post(() => {
                    Status = $"Downloading {displayFileName}... {FormatSize((long)downloadedBytes)}";
                });
            }
        });
    }

    private static string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        long bytesAbsolute = Math.Abs(bytes);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytesAbsolute, 1024)));
        double num = Math.Round(bytesAbsolute / Math.Pow(1024, place), 1);
        return $"{Math.Sign(bytes) * num} {suf[place]}";
    }
}

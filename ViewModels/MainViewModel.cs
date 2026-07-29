using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Linq;
using SvcSystems.UI.Terminal;
using Renci.SshNet;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Reflection;
using Termox.Models;
using Termox.Services;
using Avalonia.Threading;

namespace Termox.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public string ApplicationVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    private bool _isConnectionModalVisible;
    public bool IsConnectionModalVisible
    {
        get => _isConnectionModalVisible;
        set { _isConnectionModalVisible = value; OnPropertyChanged(); }
    }

    private bool _isTestSuccessful;
    public bool IsTestSuccessful
    {
        get => _isTestSuccessful;
        set { _isTestSuccessful = value; OnPropertyChanged(); }
    }

    public ObservableCollection<SshConnectionProfile> SavedConnections { get; } = new();

    public ObservableCollection<BookmarkModel> Bookmarks { get; } = new();

    public ObservableCollection<ITabViewModel> Tabs { get; } = new();

    private ITabViewModel? _selectedTab;
    public ITabViewModel? SelectedTab
    {
        get => _selectedTab;
        set { _selectedTab = value; OnPropertyChanged(); }
    }

    private string _connectionName = "New Connection";
    public string ConnectionName
    {
        get => _connectionName;
        set { _connectionName = value; OnPropertyChanged(); }
    }

    private string _host = "";
    public string Host
    {
        get => _host;
        set { _host = value; IsTestSuccessful = false; OnPropertyChanged(); }
    }

    private string _port = "22";
    public string Port
    {
        get => _port;
        set { _port = value; IsTestSuccessful = false; OnPropertyChanged(); }
    }

    private string _username = "";
    public string Username
    {
        get => _username;
        set { _username = value; IsTestSuccessful = false; OnPropertyChanged(); }
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set { _password = value; IsTestSuccessful = false; OnPropertyChanged(); }
    }

    private string _privateKeyPath = "";
    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        set { _privateKeyPath = value; IsTestSuccessful = false; OnPropertyChanged(); }
    }

    private string _hostKeyFingerprint = "";
    public string HostKeyFingerprint
    {
        get => _hostKeyFingerprint;
        set { _hostKeyFingerprint = value; OnPropertyChanged(); }
    }

    private bool _isDeleteConfirmModalVisible;
    public bool IsDeleteConfirmModalVisible
    {
        get => _isDeleteConfirmModalVisible;
        set { _isDeleteConfirmModalVisible = value; OnPropertyChanged(); }
    }

    private SshConnectionProfile? _profileToDelete;
    public SshConnectionProfile? ProfileToDelete
    {
        get => _profileToDelete;
        set { _profileToDelete = value; OnPropertyChanged(); }
    }

    private bool _isBookmarkDeleteConfirmModalVisible;
    public bool IsBookmarkDeleteConfirmModalVisible
    {
        get => _isBookmarkDeleteConfirmModalVisible;
        set { _isBookmarkDeleteConfirmModalVisible = value; OnPropertyChanged(); }
    }

    private BookmarkModel? _bookmarkToDelete;
    public BookmarkModel? BookmarkToDelete
    {
        get => _bookmarkToDelete;
        set { _bookmarkToDelete = value; OnPropertyChanged(); }
    }

    private bool _isCloseConfirmModalVisible;
    public bool IsCloseConfirmModalVisible
    {
        get => _isCloseConfirmModalVisible;
        set { _isCloseConfirmModalVisible = value; OnPropertyChanged(); }
    }

    private ITabViewModel? _tabToClose;
    public ITabViewModel? TabToClose
    {
        get => _tabToClose;
        set { _tabToClose = value; OnPropertyChanged(); }
    }

    private bool _isRenameModalVisible;
    public bool IsRenameModalVisible
    {
        get => _isRenameModalVisible;
        set { _isRenameModalVisible = value; OnPropertyChanged(); }
    }

    private string _renameModalText = "";
    public string RenameModalText
    {
        get => _renameModalText;
        set { _renameModalText = value; OnPropertyChanged(); }
    }

    private bool _isFilePropertiesModalVisible;
    public bool IsFilePropertiesModalVisible
    {
        get => _isFilePropertiesModalVisible;
        set { _isFilePropertiesModalVisible = value; OnPropertyChanged(); }
    }

    private RemoteFileModel? _selectedFileProperties;
    public RemoteFileModel? SelectedFileProperties
    {
        get => _selectedFileProperties;
        set { _selectedFileProperties = value; OnPropertyChanged(); }
    }

    private bool _isFilePreviewModalVisible;
    public bool IsFilePreviewModalVisible
    {
        get => _isFilePreviewModalVisible;
        set { _isFilePreviewModalVisible = value; OnPropertyChanged(); }
    }

    private string _filePreviewContent = "";
    public string FilePreviewContent
    {
        get => _filePreviewContent;
        set { _filePreviewContent = value; OnPropertyChanged(); }
    }

    private string _filePreviewName = "";
    public string FilePreviewName
    {
        get => _filePreviewName;
        set { _filePreviewName = value; OnPropertyChanged(); }
    }

    private bool _isPermissionsModalVisible;
    public bool IsPermissionsModalVisible
    {
        get => _isPermissionsModalVisible;
        set { _isPermissionsModalVisible = value; OnPropertyChanged(); }
    }

    private RemoteFileModel? _selectedFileForPermissions;
    public RemoteFileModel? SelectedFileForPermissions
    {
        get => _selectedFileForPermissions;
        set { _selectedFileForPermissions = value; OnPropertyChanged(); }
    }

    private string _testStatus = "";
    public string TestStatus
    {
        get => _testStatus;
        set { _testStatus = value; OnPropertyChanged(); }
    }

    private string _testStatusColor = "#5bc0de";
    public string TestStatusColor
    {
        get => _testStatusColor;
        set { _testStatusColor = value; OnPropertyChanged(); }
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ShowConnectionModalCommand { get; }
    public ICommand HideConnectionModalCommand { get; }
    public ICommand SaveConnectionCommand { get; }
    public ICommand ConnectProfileCommand { get; }
    public ICommand ConnectSftpProfileCommand { get; }
    public ICommand OpenBookmarkTerminalCommand { get; }
    public ICommand OpenBookmarkSftpCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand RequestCloseTabCommand { get; }
    public ICommand ConfirmCloseTabCommand { get; }
    public ICommand CancelCloseTabCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand RemoveBookmarkCommand { get; }
    public ICommand ConfirmRemoveBookmarkCommand { get; }
    public ICommand CancelRemoveBookmarkCommand { get; }
    public ICommand ClearAllBookmarksCommand { get; }
    public ICommand ShowFilePropertiesCommand { get; }
    public ICommand PreviewFileCommand { get; }
    public ICommand EditFilePermissionsCommand { get; }
    public ICommand OpenPortScannerTabCommand { get; }
    public ICommand OpenPingTestTabCommand { get; }
    public ICommand OpenSshKeyGeneratorTabCommand { get; }
    public ICommand OpenConnectionTesterTabCommand { get; }
    public ICommand OpenSshEndpointTestTabCommand { get; }

    private string _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "connections.json");
    private string _bookmarksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "bookmarks.json");
    private string _sessionsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "sessions.json");

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(() => Connect());
        DisconnectCommand = new RelayCommand(() => SelectedTab?.DisconnectCommand.Execute(null));
        ShowConnectionModalCommand = new RelayCommand(() => { IsConnectionModalVisible = true; TestStatus = ""; TestStatusColor = "#5bc0de"; IsTestSuccessful = false; });
        HideConnectionModalCommand = new RelayCommand(() => IsConnectionModalVisible = false);
        SaveConnectionCommand = new RelayCommand(SaveConnection);
        ConnectProfileCommand = new RelayCommand<SshConnectionProfile>(ConnectProfile);
        ConnectSftpProfileCommand = new RelayCommand<SshConnectionProfile>(profile => ConnectSftpProfile(profile));
        OpenBookmarkTerminalCommand = new RelayCommand<BookmarkModel>(OpenBookmarkInTerminal);
        OpenBookmarkSftpCommand = new RelayCommand<BookmarkModel>(OpenBookmarkInSftp);
        DeleteProfileCommand = new RelayCommand<SshConnectionProfile>(p => { ProfileToDelete = p; IsDeleteConfirmModalVisible = true; });
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmModalVisible = false);
        RequestCloseTabCommand = new RelayCommand<ITabViewModel>(t => { TabToClose = t; IsCloseConfirmModalVisible = true; });
        ConfirmCloseTabCommand = new RelayCommand(ConfirmCloseTab);
        CancelCloseTabCommand = new RelayCommand(() => IsCloseConfirmModalVisible = false);
        TestConnectionCommand = new RelayCommand(TestConnection);
        AddBookmarkCommand = new RelayCommand<string>(AddBookmark);
        RemoveBookmarkCommand = new RelayCommand<BookmarkModel>(RequestRemoveBookmark);
        ConfirmRemoveBookmarkCommand = new RelayCommand(ConfirmRemoveBookmark);
        CancelRemoveBookmarkCommand = new RelayCommand(CancelRemoveBookmark);
        ClearAllBookmarksCommand = new RelayCommand(ClearAllBookmarks);
        ShowFilePropertiesCommand = new RelayCommand<RemoteFileModel>(ShowFileProperties);
        PreviewFileCommand = new RelayCommand<RemoteFileModel>(PreviewFile);
        EditFilePermissionsCommand = new RelayCommand<RemoteFileModel>(EditFilePermissions);
        OpenToolsTabCommand = new RelayCommand(OpenToolsTab);
        OpenPortScannerTabCommand = new RelayCommand(OpenPortScannerTab);
        OpenPingTestTabCommand = new RelayCommand(OpenPingTestTab);
        OpenSshKeyGeneratorTabCommand = new RelayCommand(OpenSshKeyGeneratorTab);
        OpenConnectionTesterTabCommand = new RelayCommand(OpenConnectionTesterTab);
        OpenSshEndpointTestTabCommand = new RelayCommand(OpenSshEndpointTestTab);
        ShowAboutDialogCommand = new RelayCommand(() => IsAboutDialogVisible = true);

        LoadConnections();
        LoadBookmarks();
        LoadAndRestoreSessions();
    }

    private void LoadConnections()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var profiles = JsonSerializer.Deserialize<SshConnectionProfile[]>(json);
                if (profiles != null)
                {
                    foreach (var p in profiles)
                    {
                        // Decrypt passwords after loading
                        var decrypted = CredentialManager.DecryptProfile(p);
                        SavedConnections.Add(decrypted);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load connections: {ex.Message}");
        }
    }

    private void SaveConnection()
    {
        if (string.IsNullOrWhiteSpace(Host) || !int.TryParse(Port, out var parsedPort) || parsedPort is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(Username))
        {
            TestStatus = "Host, username, and a valid port (1-65535) are required.";
            TestStatusColor = "#f44336";
            return;
        }

        var profile = new SshConnectionProfile
        {
            Name = string.IsNullOrWhiteSpace(ConnectionName) ? Host : ConnectionName,
            Host = Host,
            Port = parsedPort,
            Username = Username,
            Password = Password,
            PrivateKeyPath = PrivateKeyPath,
            HostKeyFingerprint = HostKeyFingerprint
        };

        var existing = SavedConnections.FirstOrDefault(c => c.Name == profile.Name && c.Host == profile.Host);
        if (existing != null) SavedConnections.Remove(existing);

        SavedConnections.Add(profile);

        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Encrypt passwords before saving
            SaveProfilesToDisk();
        }
        catch (Exception ex)
        {
            SavedConnections.Remove(profile);
            if (existing != null) SavedConnections.Add(existing);
            TestStatus = $"Could not save credentials securely: {ex.Message}";
            TestStatusColor = "#f44336";
            Console.WriteLine($"Failed to save connection: {ex.Message}");
            return;
        }

        IsConnectionModalVisible = false;
    }

    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username) ||
            !int.TryParse(Port, out var parsedPort) || parsedPort is < 1 or > 65535)
        {
            TestStatus = "Host, username, and a valid port (1-65535) are required.";
            TestStatusColor = "#f44336";
            IsTestSuccessful = false;
            return;
        }

        TestStatus = "Testing connection...";
        TestStatusColor = "#f39c12";
        IsTestSuccessful = false;
        Task.Run(() =>
        {
            try
            {
                int portNumber = parsedPort;

                SshClient testClient;
                var safeUsername = Username ?? "";
                var safePassword = Password ?? "";
                SshSecurity.EnsurePrivateKeyExists(PrivateKeyPath);

                if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && File.Exists(PrivateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(PrivateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    testClient = new SshClient(Host ?? "", portNumber, safeUsername, new[] { keyFile });
                }

                else
                {
                    testClient = new SshClient(Host ?? "", portNumber, safeUsername, safePassword);
                }

                testClient.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;

                SshSecurity.ConfigureHostKeyPolicy(testClient, HostKeyFingerprint,
                    fingerprint => HostKeyFingerprint = fingerprint);
                var connectTask = Task.Run(() =>
                {
                    try { testClient.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                    finally
                    {
                        try { testClient.Disconnect(); } catch { }
                        testClient.Dispose();
                    }
                });
                var completed = Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult();
                if (completed != connectTask)
                {
                    try { testClient.Dispose(); } catch { }
                    _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    TestStatus = "Test timed out after 10 seconds.";
                    TestStatusColor = "#f39c12";
                    return;
                }
                connectTask.GetAwaiter().GetResult();

                TestStatus = "Test Successful!";
                TestStatusColor = "#4caf50";
                IsTestSuccessful = true;
            }
            catch (Exception ex)
            {
                TestStatus = "Test Failed: " + ex.Message;
                TestStatusColor = "#f44336";
                IsTestSuccessful = false;
            }
        });
    }

    private void ConnectProfile(SshConnectionProfile profile)
    {
        Host = profile.Host;
        Port = profile.Port.ToString();
        Username = profile.Username;
        Password = profile.Password;
        PrivateKeyPath = profile.PrivateKeyPath;
        HostKeyFingerprint = profile.HostKeyFingerprint;
        Connect(profile.Id);
    }

    public void OpenBookmarkInTerminal(BookmarkModel bookmark)
    {
        if (bookmark == null || string.IsNullOrWhiteSpace(bookmark.Path) || string.IsNullOrWhiteSpace(bookmark.ProfileId)) return;

        var profile = SavedConnections.FirstOrDefault(p => p.Id == bookmark.ProfileId);
        if (profile == null) return;

        Host = profile.Host;
        Port = profile.Port.ToString();
        Username = profile.Username;
        Password = profile.Password;
        PrivateKeyPath = profile.PrivateKeyPath;
        HostKeyFingerprint = profile.HostKeyFingerprint;

        var escapedPath = bookmark.Path.Replace("'", "'\\''", StringComparison.Ordinal);
        Connect(profile.Id, $"cd '{escapedPath}'");
    }

    private void OpenBookmarkInSftp(BookmarkModel bookmark)
    {
        if (bookmark == null || string.IsNullOrWhiteSpace(bookmark.ProfileId)) return;

        var profile = SavedConnections.FirstOrDefault(p => p.Id == bookmark.ProfileId);
        if (profile != null)
            ConnectSftpProfile(profile, bookmark.Path);
    }

    private void ConnectSftpProfile(SshConnectionProfile profile, string? initialPath = null)
    {
        var tab = new SftpTabViewModel(t => { Tabs.Remove(t); SaveCurrentSessions(); });
        Tabs.Add(tab);
        SelectedTab = tab;

        tab.ConnectionProfileId = profile.Id;
        tab.Connect(profile.Host, profile.Port, profile.Username, profile.Password, profile.PrivateKeyPath,
            profile.HostKeyFingerprint, fingerprint => RememberHostKey(profile, fingerprint), initialPath);
        SaveCurrentSessions();
    }

    private void ConfirmDelete()
    {
        if (ProfileToDelete != null)
        {
            SavedConnections.Remove(ProfileToDelete);
            try
            {
                SaveProfilesToDisk();
            }
            catch (Exception ex) { Console.WriteLine($"Failed to update connections file on delete: {ex.Message}"); }
        }
        IsDeleteConfirmModalVisible = false;
        ProfileToDelete = null;
    }

    private void ConfirmCloseTab()
    {
        if (TabToClose != null)
        {
            TabToClose.CloseTabCommand.Execute(null);
        }
        IsCloseConfirmModalVisible = false;
        TabToClose = null;
        SaveCurrentSessions();
    }

    private void Connect(string? profileId = null, string? initialCommand = null)
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username) ||
            !int.TryParse(Port, out var portNumber) || portNumber is < 1 or > 65535)
        {
            TestStatus = "Host, username, and a valid port (1-65535) are required.";
            TestStatusColor = "#f44336";
            return;
        }

        IsConnectionModalVisible = false;

        var tab = new TerminalTabViewModel(t => { Tabs.Remove(t); SaveCurrentSessions(); });
        tab.ConnectionProfileId = profileId;
        Tabs.Add(tab);
        SelectedTab = tab;

        tab.Connect(Host ?? "", portNumber, Username ?? "", Password ?? "", PrivateKeyPath ?? "",
            HostKeyFingerprint, fingerprint =>
            {
                HostKeyFingerprint = fingerprint;
                if (profileId != null)
                {
                    var profile = SavedConnections.FirstOrDefault(p => p.Id == profileId);
                    if (profile != null) RememberHostKey(profile, fingerprint);
                }
            }, initialCommand);
        SaveCurrentSessions();
    }

    public ICommand OpenToolsTabCommand { get; private set; }
    public ICommand ShowAboutDialogCommand { get; }

    private bool _isAboutDialogVisible;
    public bool IsAboutDialogVisible
    {
        get => _isAboutDialogVisible;
        set { _isAboutDialogVisible = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }

    private void LoadBookmarks()
    {
        try
        {
            if (File.Exists(_bookmarksPath))
            {
                var json = File.ReadAllText(_bookmarksPath);
                try
                {
                    var bookmarks = JsonSerializer.Deserialize<BookmarkModel[]>(json);
                    if (bookmarks != null)
                    {
                        foreach (var bookmark in bookmarks) Bookmarks.Add(bookmark);
                    }
                }
                catch (JsonException)
                {
                    // Migrate the previous path-only bookmark format.
                    var paths = JsonSerializer.Deserialize<string[]>(json);
                    if (paths != null)
                    {
                        foreach (var path in paths)
                            Bookmarks.Add(new BookmarkModel { Path = path });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load bookmarks: {ex.Message}");
        }
    }

    private void SaveBookmarks()
    {
        try
        {
            var dir = Path.GetDirectoryName(_bookmarksPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_bookmarksPath, JsonSerializer.Serialize(Bookmarks.ToList()));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save bookmarks: {ex.Message}");
        }
    }

    private void AddBookmark(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var sftpTab = SelectedTab as SftpTabViewModel;
        if (sftpTab?.ConnectionProfileId == null) return;

        AddBookmarkForProfile(path, sftpTab.ConnectionProfileId);
    }

    public void AddBookmarkForProfile(string path, string profileId)
    {
        if (!IsValidRemoteDirectory(path) || string.IsNullOrWhiteSpace(profileId)) return;

        var profile = SavedConnections.FirstOrDefault(p => p.Id == profileId);
        if (profile == null || Bookmarks.Any(b => b.Path == path && b.ProfileId == profile.Id)) return;

        Bookmarks.Add(new BookmarkModel
        {
            Path = path,
            ProfileId = profile.Id,
            SessionName = profile.Name
        });
        SaveBookmarks();
    }

    public void OpenSftpFromTerminal(TerminalTabViewModel terminal, string path)
    {
        if (string.IsNullOrWhiteSpace(terminal.ConnectionProfileId) || !IsValidRemoteDirectory(path)) return;

        var profile = SavedConnections.FirstOrDefault(p => p.Id == terminal.ConnectionProfileId);
        if (profile != null)
            ConnectSftpProfile(profile, path);
    }

    private static bool IsValidRemoteDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            path.StartsWith("/", StringComparison.Ordinal) &&
            !path.Contains("%s", StringComparison.Ordinal) &&
            !path.Contains("$PWD", StringComparison.Ordinal) &&
            !path.Contains("\\n", StringComparison.Ordinal);
    }

    private void RequestRemoveBookmark(BookmarkModel bookmark)
    {
        if (bookmark == null || !Bookmarks.Contains(bookmark)) return;
        BookmarkToDelete = bookmark;
        IsBookmarkDeleteConfirmModalVisible = true;
    }

    private void ConfirmRemoveBookmark()
    {
        if (BookmarkToDelete != null && Bookmarks.Contains(BookmarkToDelete))
        {
            Bookmarks.Remove(BookmarkToDelete);
            SaveBookmarks();
        }

        IsBookmarkDeleteConfirmModalVisible = false;
        BookmarkToDelete = null;
    }

    private void CancelRemoveBookmark()
    {
        IsBookmarkDeleteConfirmModalVisible = false;
        BookmarkToDelete = null;
    }

    private void ClearAllBookmarks()
    {
        Bookmarks.Clear();
        SaveBookmarks();
    }

    private void LoadAndRestoreSessions()
    {
        try
        {
            if (File.Exists(_sessionsPath))
            {
                var json = File.ReadAllText(_sessionsPath);
                var sessions = JsonSerializer.Deserialize<SessionData[]>(json);
                if (sessions != null && sessions.Length > 0)
                {
                    foreach (var session in sessions)
                    {
                        if (session.Type == "terminal")
                        {
                            var profile = FindSessionProfile(session);
                            if (profile != null)
                            {
                                ConnectProfile(profile);
                            }
                        }
                        else if (session.Type == "sftp")
                        {
                            var profile = FindSessionProfile(session);
                            if (profile != null)
                            {
                                ConnectSftpProfile(profile);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to restore sessions: {ex.Message}");
        }
    }

    private void SaveCurrentSessions()
    {
        try
        {
            var sessions = new System.Collections.Generic.List<SessionData>();
            foreach (var tab in Tabs)
            {
                if (tab is TerminalTabViewModel termTab && !string.IsNullOrWhiteSpace(termTab.ConnectionHost))
                {
                    sessions.Add(new SessionData
                    {
                        Type = "terminal", ProfileId = termTab.ConnectionProfileId,
                        Host = termTab.ConnectionHost, Port = termTab.ConnectionPort,
                        Username = termTab.ConnectionUsername
                    });
                }
                else if (tab is SftpTabViewModel sftpTab && !string.IsNullOrWhiteSpace(sftpTab.ConnectionHost))
                {
                    sessions.Add(new SessionData
                    {
                        Type = "sftp", ProfileId = sftpTab.ConnectionProfileId,
                        Host = sftpTab.ConnectionHost, Port = sftpTab.ConnectionPort,
                        Username = sftpTab.ConnectionUsername
                    });
                }
            }

            var dir = Path.GetDirectoryName(_sessionsPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(sessions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save sessions: {ex.Message}");
        }
    }

    private void ShowFileProperties(RemoteFileModel? file)
    {
        if (file == null) return;
        SelectedFileProperties = file;
        IsFilePropertiesModalVisible = true;
    }

    private void PreviewFile(RemoteFileModel? file)
    {
        if (file == null || file.IsDirectory) return;

        if (SelectedTab is SftpTabViewModel vm)
        {
            Task.Run(() => vm.PreviewFile(file, this));
        }
    }

    private void EditFilePermissions(RemoteFileModel? file)
    {
        if (file == null) return;
        SelectedFileForPermissions = file;
        IsPermissionsModalVisible = true;
    }

    private void OpenToolsTab()
    {
        var toolsTab = new ToolsTabViewModel(t => { Tabs.Remove(t); SaveCurrentSessions(); });
        toolsTab.SetConnections(SavedConnections);
        Tabs.Add(toolsTab);
        SelectedTab = toolsTab;
    }

    private void OpenPortScannerTab()
    {
        var scannerTab = new PortScannerTabViewModel(tab =>
        {
            Tabs.Remove(tab);
            SaveCurrentSessions();
        }, SavedConnections);
        Tabs.Add(scannerTab);
        SelectedTab = scannerTab;
    }

    private void OpenPingTestTab()
    {
        var pingTab = new PingTestTabViewModel(tab =>
        {
            Tabs.Remove(tab);
            SaveCurrentSessions();
        });
        pingTab.SetConnections(SavedConnections);
        Tabs.Add(pingTab);
        SelectedTab = pingTab;
    }

    private void OpenSshKeyGeneratorTab()
    {
        var keyTab = new SshKeyGeneratorTabViewModel(tab =>
        {
            Tabs.Remove(tab);
            SaveCurrentSessions();
        });
        keyTab.SetConnections(SavedConnections);
        Tabs.Add(keyTab);
        SelectedTab = keyTab;
    }

    private void OpenConnectionTesterTab()
    {
        var testerTab = new ConnectionTesterTabViewModel(tab =>
        {
            Tabs.Remove(tab);
            SaveCurrentSessions();
        });
        testerTab.SetConnections(SavedConnections);
        Tabs.Add(testerTab);
        SelectedTab = testerTab;
    }

    private void OpenSshEndpointTestTab()
    {
        var endpointTab = new SshEndpointTestTabViewModel(tab =>
        {
            Tabs.Remove(tab);
            SaveCurrentSessions();
        });
        endpointTab.SetConnections(SavedConnections);
        Tabs.Add(endpointTab);
        SelectedTab = endpointTab;
    }

    private void RememberHostKey(SshConnectionProfile profile, string fingerprint)
    {
        profile.HostKeyFingerprint = fingerprint;
        SaveProfilesToDisk();
    }

    private void SaveProfilesToDisk()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var profilesToSave = SavedConnections.Select(p => CredentialManager.EncryptProfile(p)).ToList();
        File.WriteAllText(_configPath, JsonSerializer.Serialize(profilesToSave));
    }

    private SshConnectionProfile? FindSessionProfile(SessionData session)
    {
        return (!string.IsNullOrWhiteSpace(session.ProfileId)
                ? SavedConnections.FirstOrDefault(c => c.Id == session.ProfileId)
                : null)
            ?? SavedConnections.FirstOrDefault(c =>
                c.Host == session.Host && c.Port == session.Port && c.Username == session.Username);
    }
}

public class SessionData
{
    public string Type { get; set; } = ""; // "terminal" or "sftp"
    public string? ProfileId { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

    public void Execute(object? parameter) => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute == null || (parameter is T t && _canExecute(t));

    public void Execute(object? parameter)
    {
        if (parameter is T t)
        {
            _execute(t);
        }
    }
}

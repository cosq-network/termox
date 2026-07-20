using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Linq;
using AvaloniaTerminal;
using Renci.SshNet;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Termox.Models;
using Avalonia.Threading;

namespace Termox.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private SshClient? _sshClient;
    private ShellStream? _shellStream;

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

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = new();

    private TerminalTabViewModel? _selectedTab;
    public TerminalTabViewModel? SelectedTab
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

    private bool _isCloseConfirmModalVisible;
    public bool IsCloseConfirmModalVisible
    {
        get => _isCloseConfirmModalVisible;
        set { _isCloseConfirmModalVisible = value; OnPropertyChanged(); }
    }

    private TerminalTabViewModel? _tabToClose;
    public TerminalTabViewModel? TabToClose
    {
        get => _tabToClose;
        set { _tabToClose = value; OnPropertyChanged(); }
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
    public ICommand DeleteProfileCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand RequestCloseTabCommand { get; }
    public ICommand ConfirmCloseTabCommand { get; }
    public ICommand CancelCloseTabCommand { get; }
    public ICommand TestConnectionCommand { get; }

    private string _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "connections.json");

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(Connect);
        DisconnectCommand = new RelayCommand(() => SelectedTab?.DisconnectCommand.Execute(null));
        ShowConnectionModalCommand = new RelayCommand(() => { IsConnectionModalVisible = true; TestStatus = ""; TestStatusColor = "#5bc0de"; IsTestSuccessful = false; });
        HideConnectionModalCommand = new RelayCommand(() => IsConnectionModalVisible = false);
        SaveConnectionCommand = new RelayCommand(SaveConnection);
        ConnectProfileCommand = new RelayCommand<SshConnectionProfile>(ConnectProfile);
        DeleteProfileCommand = new RelayCommand<SshConnectionProfile>(p => { ProfileToDelete = p; IsDeleteConfirmModalVisible = true; });
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmModalVisible = false);
        RequestCloseTabCommand = new RelayCommand<TerminalTabViewModel>(t => { TabToClose = t; IsCloseConfirmModalVisible = true; });
        ConfirmCloseTabCommand = new RelayCommand(ConfirmCloseTab);
        CancelCloseTabCommand = new RelayCommand(() => IsCloseConfirmModalVisible = false);
        TestConnectionCommand = new RelayCommand(TestConnection);

        LoadConnections();


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
                    foreach (var p in profiles) SavedConnections.Add(p);
                }
            }
        }
        catch { }
    }

    private void SaveConnection()
    {
        var profile = new SshConnectionProfile
        {
            Name = string.IsNullOrWhiteSpace(ConnectionName) ? Host : ConnectionName,
            Host = Host,
            Port = int.TryParse(Port, out int p) ? p : 22,
            Username = Username,
            Password = Password,
            PrivateKeyPath = PrivateKeyPath
        };

        var existing = SavedConnections.FirstOrDefault(c => c.Name == profile.Name && c.Host == profile.Host);
        if (existing != null) SavedConnections.Remove(existing);
        
        SavedConnections.Add(profile);

        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(SavedConnections));
        }
        catch { }
        
        IsConnectionModalVisible = false;
    }

    private void TestConnection()
    {
        TestStatus = "Testing connection...";
        TestStatusColor = "#f39c12";
        IsTestSuccessful = false;
        Task.Run(() =>
        {
            try
            {
                if (!int.TryParse(Port, out int portNumber)) portNumber = 22;

                SshClient testClient;
                var safeUsername = Username ?? "";
                var safePassword = Password ?? "";
                
                if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && File.Exists(PrivateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(PrivateKeyPath, string.IsNullOrEmpty(safePassword) ? null : safePassword);
                    testClient = new SshClient(Host ?? "", portNumber, safeUsername, new[] { keyFile });
                }
                else
                {
                    testClient = new SshClient(Host ?? "", portNumber, safeUsername, safePassword);
                }
                
                testClient.Connect();
                testClient.Disconnect();
                testClient.Dispose();

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
        Connect();
    }

    private void ConfirmDelete()
    {
        if (ProfileToDelete != null)
        {
            SavedConnections.Remove(ProfileToDelete);
            try { File.WriteAllText(_configPath, JsonSerializer.Serialize(SavedConnections)); } catch { }
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
    }

    private void Connect()
    {
        IsConnectionModalVisible = false;
        
        var tab = new TerminalTabViewModel(t => Tabs.Remove(t));
        Tabs.Add(tab);
        SelectedTab = tab;

        if (!int.TryParse(Port, out int portNumber)) portNumber = 22;
        tab.Connect(Host ?? "", portNumber, Username ?? "", Password ?? "", PrivateKeyPath ?? "");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
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

    public bool CanExecute(object? parameter) => _canExecute == null || (parameter is T t && _canExecute(t));

    public void Execute(object? parameter)
    {
        if (parameter is T t)
        {
            _execute(t);
        }
    }
}

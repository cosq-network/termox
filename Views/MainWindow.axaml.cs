using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using Termox.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Termox.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowsePrivateKey_Click(object? sender, RoutedEventArgs e)
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        IStorageFolder? startLocation = null;
        if (Directory.Exists(sshDir))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri($"file://{sshDir}"));
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Private Key File",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        if (files.Count >= 1)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PrivateKeyPath = files[0].Path.LocalPath;
            }
        }
    }

    private async void CopyMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null)
        {
            var text = vm.SelectedTab.TerminalModel.SelectedText;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(text))
            {
                await clipboard.SetTextAsync(text);
                vm.SelectedTab.TerminalModel.ClearSelection();
            }
        }
    }
}
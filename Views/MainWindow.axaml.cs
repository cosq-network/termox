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
        if (DataContext is MainViewModel vm && vm.SelectedTab is TerminalTabViewModel termTab)
        {
            var text = termTab.TerminalModel.SelectedText;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(text))
            {
                await clipboard.SetTextAsync(text);
                termTab.TerminalModel.ClearSelection();
            }
        }
    }

    private async void DownloadFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SftpTabViewModel vm)
        {
            if (vm.SelectedFile == null) return;

            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Destination" });
            if (folder.Count > 0)
            {
                await vm.DownloadFileAsync(vm.SelectedFile.FullName, folder[0].Path.LocalPath, vm.SelectedFile.IsDirectory);
            }
        }
    }

    private async void UploadFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SftpTabViewModel vm)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Select Files to Upload" });
            if (files.Count > 0)
            {
                foreach (var f in files)
                {
                    await vm.UploadFileAsync(f.Path.LocalPath);
                }
            }
        }
    }

    private void SftpFileList_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is ListBox listbox && listbox.DataContext is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            if (vm.NavigateToCommand.CanExecute(vm.SelectedFile))
            {
                vm.NavigateToCommand.Execute(vm.SelectedFile);
            }
        }
    }
}
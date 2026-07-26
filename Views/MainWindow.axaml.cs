using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using Termox.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using System.Linq;
using Termox.Models;

namespace Termox.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            foreach (var tab in vm.Tabs.ToList())
                tab.DisconnectCommand.Execute(null);
        }
    }

    private void ExitTermox_Click(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
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
            var selectedItems = btn.CommandParameter as System.Collections.IList;
            if (selectedItems == null || selectedItems.Count == 0) return;

            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Destination" });
            if (folder.Count > 0)
            {
                await vm.DownloadFilesAsync(selectedItems, folder[0].Path.LocalPath);
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
                var paths = new System.Collections.Generic.List<string>();
                foreach (var f in files) paths.Add(f.Path.LocalPath);
                await vm.UploadFilesAsync(paths);
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

    private void SftpFileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listbox && listbox.DataContext is SftpTabViewModel vm)
        {
            vm.UpdateCanDownload(listbox.SelectedItems?.Count > 0);
            vm.UpdateSelection(listbox.SelectedItems?.Cast<RemoteFileModel>() ?? Enumerable.Empty<RemoteFileModel>());
        }
    }

    private void RenameFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            if (DataContext is MainViewModel mainVm)
            {
                mainVm.RenameModalText = vm.SelectedFile.Name;
                mainVm.IsRenameModalVisible = true;
                _renameFileVm = vm;
                _renamedOldName = vm.SelectedFile.Name;
            }
        }
    }

    private SftpTabViewModel? _renameFileVm;
    private string _renamedOldName = "";

    private void ConfirmRename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && _renameFileVm != null && !string.IsNullOrWhiteSpace(mainVm.RenameModalText))
        {
            _renameFileVm.SetRenameNewName(mainVm.RenameModalText);
            _renameFileVm.RenameFileCommand.Execute(null);
            mainVm.IsRenameModalVisible = false;
            mainVm.RenameModalText = "";
        }
    }

    private void RemoveBookmark_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && DataContext is MainViewModel vm && btn.CommandParameter is string path)
        {
            vm.RemoveBookmarkCommand.Execute(path);
        }
    }

    private void CancelRename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsRenameModalVisible = false;
            vm.RenameModalText = "";
        }
    }

    private void AddBookmark_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SftpTabViewModel vm && DataContext is MainViewModel mainVm)
        {
            mainVm.AddBookmarkCommand.Execute(vm.CurrentPath);
        }
    }

    private void SftpFileList_DragEnter(object? sender, DragEventArgs e)
    {
        // Drag & drop support will be implemented in a future version
    }

    private void SftpFileList_DragOver(object? sender, DragEventArgs e)
    {
        // Drag & drop support will be implemented in a future version
    }

    private void SftpFileList_DragLeave(object? sender, DragEventArgs e)
    {
        // Drag & drop support will be implemented in a future version
    }

    private void SftpFileList_Drop(object? sender, DragEventArgs e)
    {
        // Drag & drop support will be implemented in a future version
    }

    private async void SftpFileList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is ListBox lb && lb.DataContext is SftpTabViewModel vm)
        {
            if (e.Key == Key.F2 && vm.SelectedFile != null)
            {
                // F2 - Rename
                RenameFile_Click(new Button { DataContext = vm }, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control)
            {
                if (e.Key == Key.U)
                {
                    // Ctrl+U - Upload
                    UploadFile_Click(new Button { DataContext = vm }, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.D && vm.SelectedFile != null)
                {
                    // Ctrl+D - Download
                    DownloadFile_Click(new Button { DataContext = vm, CommandParameter = lb.SelectedItems }, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.S && DataContext is MainViewModel mainVm)
                {
                    // Ctrl+S - Save bookmark
                    mainVm.AddBookmarkCommand.Execute(vm.CurrentPath);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete && vm.SelectedFile != null)
            {
                // Delete or Ctrl+Delete - Delete file
                vm.DeleteFileCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void ContextMenu_Download(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            DownloadFile_Click(new Button { DataContext = vm, CommandParameter = new[] { vm.SelectedFile } }, e);
        }
    }

    private void ContextMenu_Upload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm)
        {
            UploadFile_Click(new Button { DataContext = vm }, e);
        }
    }

    private void ContextMenu_Rename(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            RenameFile_Click(new Button { DataContext = vm }, e);
        }
    }

    private void ContextMenu_Delete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            vm.DeleteFileCommand.Execute(null);
        }
    }

    private void ContextMenu_Preview(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            mainVm.PreviewFileCommand.Execute(vm.SelectedFile);
        }
    }

    private void ContextMenu_Properties(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            mainVm.ShowFilePropertiesCommand.Execute(vm.SelectedFile);
        }
    }

    private void ContextMenu_AddBookmark(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm)
        {
            mainVm.AddBookmarkCommand.Execute(vm.CurrentPath);
        }
    }

    private void ClosePropertiesModal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsFilePropertiesModalVisible = false;
        }
    }

    private void ClosePreviewModal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsFilePreviewModalVisible = false;
        }
    }

    private void EditPermissions_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            mainVm.EditFilePermissionsCommand.Execute(vm.SelectedFile);
        }
    }

    private void ClosePermissionsModal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsPermissionsModalVisible = false;
        }
    }

    private void SetPermissions_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string perms)
        {
            if (this.FindControl<TextBox>("PermissionsInput") is TextBox tb)
            {
                tb.Text = perms;
            }
        }
    }

    private void ApplyPermissions_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && mainVm.SelectedFileForPermissions != null)
        {
            if (this.FindControl<TextBox>("PermissionsInput") is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                // Parse octal string (e.g., "755" -> 0o755 in C#)
                if (int.TryParse(tb.Text, out int decimalMode))
                {
                    // Convert decimal input to octal interpretation
                    int octalMode = 0;
                    int temp = decimalMode;
                    int multiplier = 1;

                    while (temp > 0)
                    {
                        octalMode += (temp % 10) * multiplier;
                        temp /= 10;
                        multiplier *= 8;
                    }

                    vm.ChangeFilePermissions(mainVm.SelectedFileForPermissions, (short)octalMode, mainVm);
                    mainVm.IsPermissionsModalVisible = false;
                }
            }
            else
            {
                mainVm.SelectedFileForPermissions = null;
                mainVm.IsPermissionsModalVisible = false;
            }
        }
    }

    private void CloseAboutDialog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsAboutDialogVisible = false;
        }
    }

}

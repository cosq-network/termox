using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Threading.Tasks;
using Termox.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using System.Linq;
using Termox.Models;
using SvcSystems.UI.Terminal;

namespace Termox.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var disconnectTasks = vm.Tabs
                .OfType<SftpTabViewModel>()
                .Select(tab => tab.DisconnectAsync())
                .ToArray();

            foreach (var tab in vm.Tabs.Where(tab => tab is not SftpTabViewModel).ToList())
                tab.DisconnectCommand.Execute(null);

            await Task.WhenAll(disconnectTasks);
        }
    }

    private void ExitTermox_Click(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void SavedSession_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control &&
            control.DataContext is SshConnectionProfile profile &&
            DataContext is MainViewModel vm)
        {
            vm.ConnectProfileCommand.Execute(profile);
        }
    }

    private void Bookmark_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control &&
            control.DataContext is BookmarkModel bookmark &&
            DataContext is MainViewModel vm)
        {
            vm.OpenBookmarkInTerminal(bookmark);
        }
    }

    private async void BrowsePrivateKey_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            IStorageFolder? startLocation = null;
            if (Directory.Exists(sshDir))
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri($"file://{sshDir}"));

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Private Key File",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });

            if (files.Count >= 1 && DataContext is MainViewModel vm)
                vm.PrivateKeyPath = files[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Private key picker failed: {ex.Message}");
        }
    }

    private async void CopyMenu_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is TerminalControl terminal)
            {
                var text = terminal.SelectedText;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null && !string.IsNullOrEmpty(text))
                {
                    await clipboard.SetTextAsync(text);
                    terminal.Model?.ClearSelection();
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"Clipboard copy failed: {ex.Message}"); }
    }

    private async void TerminalContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not TerminalControl terminal)
            return;

        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                item.Name == "TerminalCopyMenuItem") is MenuItem copyItem)
            copyItem.IsEnabled = terminal.HasSelection;

        var hasClipboardText = false;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            hasClipboardText = clipboard != null &&
                !string.IsNullOrEmpty(await clipboard.TryGetTextAsync());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Clipboard availability check failed: {ex.Message}");
        }

        if (contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                item.Name == "TerminalPasteMenuItem") is MenuItem pasteItem)
            pasteItem.IsEnabled = hasClipboardText;
    }

    private async void PasteMenu_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is TerminalControl terminal)
            {
                await terminal.PasteFromClipboardAsync();
            }
        }
        catch (Exception ex) { Console.WriteLine($"Clipboard paste failed: {ex.Message}"); }
    }

    private async void Terminal_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TerminalControl terminal)
            return;

        var primaryModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var isPaste = e.Key == Key.V && (e.KeyModifiers & primaryModifier) != 0;
        var isDuplicate = e.Key == Key.D &&
            (e.KeyModifiers & primaryModifier) != 0 &&
            (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (!isPaste && !isDuplicate)
            return;

        e.Handled = true;

        if (isDuplicate)
        {
            if (DataContext is MainViewModel vm && terminal.DataContext is TerminalTabViewModel tab)
                vm.DuplicateTerminal(tab);
            return;
        }

        try
        {
            await terminal.PasteFromClipboardAsync();
        }
        catch (Exception ex) { Console.WriteLine($"Clipboard paste failed: {ex.Message}"); }
    }

    private void DuplicateTerminalMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not TerminalControl terminal ||
            terminal.DataContext is not TerminalTabViewModel tab)
            return;

        vm.DuplicateTerminal(tab);
    }

    private async void OpenSftpFromTerminal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm || mainVm.SelectedTab is not TerminalTabViewModel terminal)
            return;

        var path = await terminal.GetCurrentDirectoryAsync();
        if (!string.IsNullOrWhiteSpace(path))
            mainVm.OpenSftpFromTerminal(terminal, path);
    }

    private async void AddTerminalDirectoryBookmark_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm || mainVm.SelectedTab is not TerminalTabViewModel terminal ||
            string.IsNullOrWhiteSpace(terminal.ConnectionProfileId))
            return;

        var path = await terminal.GetCurrentDirectoryAsync();
        if (!string.IsNullOrWhiteSpace(path))
            mainVm.AddBookmarkForProfile(path, terminal.ConnectionProfileId);
    }

    private async void DownloadFile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.DataContext is SftpTabViewModel vm)
            {
                var selectedItems = btn.CommandParameter as System.Collections.IList;
                if (selectedItems == null || selectedItems.Count == 0) return;

                var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Destination" });
                if (folder.Count > 0)
                    await vm.DownloadFilesAsync(selectedItems, folder[0].Path.LocalPath);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Download action failed: {ex.Message}"); }
    }

    private async void UploadFile_Click(object? sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex) { Console.WriteLine($"Upload action failed: {ex.Message}"); }
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

    private void SftpPathBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox textBox && textBox.DataContext is SftpTabViewModel vm)
        {
            vm.NavigateToPath(textBox.Text);
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
        if (sender is Button btn && DataContext is MainViewModel vm && btn.CommandParameter is BookmarkModel bookmark)
        {
            vm.RemoveBookmarkCommand.Execute(bookmark);
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
                TriggerRename(vm);
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control)
            {
                if (e.Key == Key.U)
                {
                    await TriggerUpload(vm);
                    e.Handled = true;
                }
                else if (e.Key == Key.D && vm.SelectedFile != null)
                {
                    await TriggerDownload(vm, lb.SelectedItems);
                    e.Handled = true;
                }
                else if (e.Key == Key.S && DataContext is MainViewModel mainVm)
                {
                    mainVm.AddBookmarkCommand.Execute(vm.CurrentPath);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete && vm.SelectedFile != null)
            {
                vm.DeleteFileCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void TriggerRename(SftpTabViewModel vm)
    {
        if (DataContext is MainViewModel mainVm && vm.SelectedFile != null)
        {
            mainVm.RenameModalText = vm.SelectedFile.Name;
            mainVm.IsRenameModalVisible = true;
            _renameFileVm = vm;
            _renamedOldName = vm.SelectedFile.Name;
        }
    }

    private async Task TriggerUpload(SftpTabViewModel vm)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Select Files to Upload" });
            if (files.Count > 0)
            {
                var paths = new System.Collections.Generic.List<string>();
                foreach (var f in files) paths.Add(f.Path.LocalPath);
                await vm.UploadFilesAsync(paths);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Upload action failed: {ex.Message}"); }
    }

    private async Task TriggerDownload(SftpTabViewModel vm, System.Collections.IList? selectedItems)
    {
        try
        {
            if (selectedItems == null || selectedItems.Count == 0) return;
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Destination" });
            if (folder.Count > 0)
                await vm.DownloadFilesAsync(selectedItems, folder[0].Path.LocalPath);
        }
        catch (Exception ex) { Console.WriteLine($"Download action failed: {ex.Message}"); }
    }

    private void ContextMenu_Download(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            _ = TriggerDownload(vm, new[] { vm.SelectedFile });
        }
    }

    private void ContextMenu_Upload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm)
        {
            _ = TriggerUpload(vm);
        }
    }

    private void ContextMenu_Rename(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            TriggerRename(vm);
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

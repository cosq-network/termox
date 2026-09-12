using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Termox.ViewModels;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using System.Linq;
using System.Windows.Input;
using Termox.Models;
using SvcSystems.UI.Terminal;
using AvaloniaEdit;

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
            vm.SaveCurrentSessions();

            var disconnectTasks = vm.Tabs
                .OfType<SftpTabViewModel>()
                .Select(tab => tab.DisconnectAsync())
                .ToArray();

            foreach (var tab in vm.Tabs.Where(tab => tab is not SftpTabViewModel).ToList())
                tab.DisconnectCommand.Execute(null);

            await Task.WhenAll(disconnectTasks);

            foreach (var tab in vm.Tabs.OfType<SftpTabViewModel>())
                tab.Dispose();
        }
    }

    private void ChatTranscript_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (scrollViewer.DataContext is not ChatTabViewModel vm) return;

        void CollectionHandler(object? s, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action != NotifyCollectionChangedAction.Add) return;
            Dispatcher.UIThread.Post(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
        }
        vm.Messages.CollectionChanged += CollectionHandler;

        // Each "You" bubble sits right above its own reply — once scrolled past one,
        // swap in a same-styled pinned copy of THAT specific question (not just whichever
        // one was sent most recently) so it stays visible while reading a long reply
        // beneath it, the way a sticky list header tracks whichever section you're
        // actually inside. Hidden again once scrolled back above every question.
        var pane = scrollViewer.Parent as Grid;
        var pinnedBar = pane?.Children.OfType<Border>().FirstOrDefault(b => b.Name == "PinnedQuestionBar");
        var pinnedText = pinnedBar?.GetLogicalDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "PinnedQuestionText");
        var pinnedChevron = pinnedBar?.GetLogicalDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "PinnedQuestionChevron");
        var itemsControl = scrollViewer.Content as ItemsControl;

        // Click toggles the pinned bar between a single ellipsis-trimmed line and full
        // wrapped text, for a question too long to read at a glance. Resets to collapsed
        // whenever a different question becomes the pinned one, so an old expanded state
        // doesn't linger over unrelated content. The chevron (and the click itself) only
        // engage when the question actually needed trimming — a short question that
        // already fits on one line has nothing to expand, so showing an affordance for it
        // is just noise.
        var isExpanded = false;
        var needsToggle = false;
        ChatMessage? pinnedMessage = null;

        void SetExpanded(bool expanded)
        {
            if (pinnedText == null || pinnedChevron == null) return;
            isExpanded = expanded;
            pinnedText.TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
            pinnedText.TextTrimming = expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            pinnedText.MaxLines = expanded ? 0 : 1;
            pinnedChevron.Text = expanded ? "⌃" : "⌄";
        }

        void PinnedBar_PointerPressed(object? s, PointerPressedEventArgs args)
        {
            if (needsToggle) SetExpanded(!isExpanded);
        }
        if (pinnedBar != null) pinnedBar.PointerPressed += PinnedBar_PointerPressed;

        void ScrollHandler(object? s, ScrollChangedEventArgs args)
        {
            if (pinnedBar == null || pinnedText == null || itemsControl == null) return;

            ChatMessage? current = null;
            for (var i = 0; i < vm.Messages.Count; i++)
            {
                if (vm.Messages[i].Role != ChatRole.User) continue;
                if (itemsControl.ContainerFromIndex(i) is not Control container) continue;

                // Bounds is relative to the ItemsControl's own panel, which for a plain
                // vertical layout equals cumulative offset within the scrollable content —
                // subtracting the current scroll offset turns that into a viewport-relative
                // position without needing a visual-tree point transform. Using Bottom (not
                // Top) here on purpose: the pinned copy should only take over once the real
                // "You" bubble has fully scrolled out of view, not the instant its top edge
                // crosses zero — otherwise the pinned bar and the still-partially-visible
                // real bubble render on top of each other for a stretch of scroll.
                var bottom = container.Bounds.Bottom - scrollViewer.Offset.Y;
                if (bottom <= 0) current = vm.Messages[i];
                else break; // messages render in order, so the first not-yet-scrolled-past one ends the search
            }

            pinnedBar.IsVisible = current != null;
            if (current != null && !ReferenceEquals(current, pinnedMessage))
            {
                pinnedMessage = current;
                pinnedText.Text = current.Content;
                SetExpanded(false);

                // Checked in the collapsed (single-line, ellipsis-trimmed) layout that
                // SetExpanded(false) just produced — HasCollapsed is Avalonia's own signal
                // that TextTrimming actually cut something, i.e. there's more to reveal.
                var layout = pinnedText.TextLayout;
                needsToggle = layout.TextLines.Count > 0 && layout.TextLines[0].HasCollapsed;
                if (pinnedChevron != null) pinnedChevron.IsVisible = needsToggle;
            }
        }

        if (pinnedBar != null) scrollViewer.ScrollChanged += ScrollHandler;

        scrollViewer.Tag = (Action)(() =>
        {
            vm.Messages.CollectionChanged -= CollectionHandler;
            scrollViewer.ScrollChanged -= ScrollHandler;
            if (pinnedBar != null) pinnedBar.PointerPressed -= PinnedBar_PointerPressed;
        });
        scrollViewer.ScrollToEnd();
    }

    private void ChatTranscript_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer { Tag: Action unsubscribe }) unsubscribe();
    }

    private async void CopyMessage_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: ChatMessage message }) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(message.Content))
                await clipboard.SetTextAsync(message.Content);
        }
        catch (Exception ex) { Console.WriteLine($"Chat message copy failed: {ex.Message}"); }
    }

    // Attached from ChatDraftInput_Loaded at Tunnel routing (not the XAML "KeyDown="
    // attribute, which is Bubble-only) — with AcceptsReturn="True", the TextBox's own
    // class handler inserts a newline and marks the event Handled during its own Bubble
    // pass *before* a Bubble-subscribed external handler on the same control ever runs,
    // so plain Enter silently only ever inserted a newline and never reached here. Running
    // at Tunnel means we decide first: handle-and-send for plain Enter (which stops the
    // TextBox's own newline logic from seeing an unhandled event), or do nothing for
    // Shift+Enter so the event continues through to the TextBox's normal newline insertion.
    private void ChatDraftInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0) return;
        if (sender is not Control control || control.DataContext is not ChatTabViewModel vm) return;

        e.Handled = true;
        if (vm.SendMessageCommand.CanExecute(null))
            vm.SendMessageCommand.Execute(null);
    }

    private void ChatDraftInput_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        textBox.AddHandler(InputElement.KeyDownEvent, ChatDraftInput_KeyDown, RoutingStrategies.Tunnel);
    }

    private void RenameTitleTextBox_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void RenameTitleTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control control) return;
        e.Handled = true;
        ConfirmRenameFor(control);
    }

    private void RenameTitleTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        ConfirmRenameFor(control);
    }

    private static void ConfirmRenameFor(Control control)
    {
        if (control.DataContext is not ChatSessionSummary summary || !summary.IsEditing) return;
        var itemsControl = control.FindAncestorOfType<ItemsControl>();
        if (itemsControl?.DataContext is ChatTabViewModel vm)
            vm.ConfirmRenameSessionCommand.Execute(summary);
    }

    private void ChatTabRoot_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        control.AddHandler(InputElement.PointerPressedEvent, ChatTabRoot_PointerPressed, RoutingStrategies.Tunnel);
    }

    // Dismisses the inline server-picker panel on a click anywhere else in the chat tab.
    // It's a plain sibling in the composer's StackPanel, not a Popup/Flyout (see the
    // comment on ServerPickerPanel for why), so it gets none of a Popup's free
    // click-outside-to-close behavior — this replicates just that one piece by hand.
    private void ChatTabRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control root || root.DataContext is not ChatTabViewModel vm) return;
        if (!vm.IsServerPickerOpen) return;

        for (var current = e.Source as Control; current != null; current = current.GetVisualParent() as Control)
        {
            if (current.Name == "ServerPickerPanel") return;
            if (current is Button button && button.Classes.Contains("chatComposerChip")) return;
        }

        vm.IsServerPickerOpen = false;
    }

    private void ExitTermox_Click(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // Custom title bar (ExtendClientAreaToDecorationsHint + NoChrome draws nothing native,
    // so window drag/minimize/maximize/close are all hand-rolled here).
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        if (this.FindControl<TextBlock>("MaximizeIcon") is { } icon)
            icon.Text = char.ConvertFromUtf32(WindowState == WindowState.Maximized ? 0xE5D1 : 0xE5D0);
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
        var isClearHistory = e.Key == Key.L && (e.KeyModifiers & primaryModifier) != 0;

        if (!isPaste && !isDuplicate && !isClearHistory)
            return;

        e.Handled = true;

        if (isClearHistory)
        {
            if (terminal.DataContext is TerminalTabViewModel tab &&
                tab.ClearHistoryCommand is ICommand cmd && cmd.CanExecute(null))
            {
                cmd.Execute(null);
            }
            return;
        }

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
            }
        }
    }

    private SftpTabViewModel? _renameFileVm;

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

    private void ToggleBookmarkFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && DataContext is MainViewModel vm && btn.CommandParameter is BookmarkModel bookmark)
        {
            vm.ToggleBookmarkFavoriteCommand.Execute(bookmark);
        }
    }

    // Tools-list drag-to-reorder. Avalonia 12's DragDrop API is IDataTransfer-based (not
    // the older IDataObject), and DoDragDropAsync needs the *original* PointerPressedEventArgs
    // as its trigger — not a later PointerMoved one — so the press handler stashes it and
    // the move handler reuses it once the drag threshold is crossed.
    private static readonly DataFormat<string> ToolDragFormat = DataFormat.CreateStringApplicationFormat("TermoxToolMenuItemId");

    private ToolMenuItem? _toolDragItem;
    private PointerPressedEventArgs? _toolDragStartArgs;
    private Point _toolDragStartPoint;
    private Control? _toolDragHandle;

    private void ToolItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // sender is the grip handle, but its DataContext is inherited from the row's
        // DataTemplate root, so it's still the bound ToolMenuItem.
        if (sender is Control handle && handle.DataContext is ToolMenuItem item &&
            e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            _toolDragItem = item;
            _toolDragStartArgs = e;
            _toolDragStartPoint = e.GetPosition(handle);
            _toolDragHandle = handle;
        }
    }

    private async void ToolItem_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_toolDragItem == null || _toolDragStartArgs == null || _toolDragHandle == null) return;
        if (!e.GetCurrentPoint(_toolDragHandle).Properties.IsLeftButtonPressed) return;

        var delta = e.GetPosition(_toolDragHandle) - _toolDragStartPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        var draggedItem = _toolDragItem;
        var startArgs = _toolDragStartArgs;
        var row = _toolDragHandle.Parent as Control ?? _toolDragHandle;
        _toolDragItem = null;
        _toolDragStartArgs = null;
        _toolDragHandle = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ToolDragFormat, draggedItem.Id));

        row.Opacity = 0.5;
        try
        {
            await DragDrop.DoDragDropAsync(startArgs, transfer, DragDropEffects.Move);
        }
        finally
        {
            row.Opacity = 1.0;
        }
    }

    private void ToolsList_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Move;
        if (sender is not ItemsControl itemsControl || DataContext is not MainViewModel mainVm) return;

        var draggedId = GetDraggedToolId(e);
        if (draggedId == null) return;

        var items = mainVm.ToolMenuItems;
        var fromIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == draggedId) { fromIndex = i; break; }
        }
        if (fromIndex < 0) return;

        var toIndex = FindToolDropIndex(itemsControl, e.GetPosition(itemsControl).Y);
        if (toIndex >= 0 && toIndex != fromIndex)
            mainVm.ReorderToolMenuItem(fromIndex, toIndex);
    }

    private void ToolsList_Drop(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Move;
    }

    private static string? GetDraggedToolId(DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(ToolDragFormat) is string id)
                return id;
        }
        return null;
    }

    private static int FindToolDropIndex(ItemsControl itemsControl, double pointerY)
    {
        for (var i = 0; i < itemsControl.ItemCount; i++)
        {
            var container = itemsControl.ContainerFromIndex(i);
            if (container == null) continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), itemsControl) ?? default;
            if (pointerY < topLeft.Y + container.Bounds.Height / 2)
                return i;
        }
        return itemsControl.ItemCount - 1;
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
            else if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
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

    private void ContextMenu_Edit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            mainVm.EditFileCommand.Execute(vm.SelectedFile);
        }
    }

    private void EditFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm && mainVm.SelectedTab is SftpTabViewModel vm && vm.SelectedFile != null)
        {
            mainVm.EditFileCommand.Execute(vm.SelectedFile);
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

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainViewModel mainVm) return;

        if (mainVm.IsFilePreviewModalVisible)
        {
            mainVm.IsFilePreviewModalVisible = false;
            e.Handled = true;
        }
        else if (mainVm.IsHostKeyConfirmModalVisible)
        {
            mainVm.RejectHostKey();
            e.Handled = true;
        }
        else if (mainVm.IsPermissionsModalVisible)
        {
            mainVm.IsPermissionsModalVisible = false;
            e.Handled = true;
        }
        else if (mainVm.IsFilePropertiesModalVisible)
        {
            mainVm.IsFilePropertiesModalVisible = false;
            e.Handled = true;
        }
        else if (mainVm.IsRenameModalVisible)
        {
            mainVm.IsRenameModalVisible = false;
            mainVm.RenameModalText = "";
            e.Handled = true;
        }
        else if (mainVm.IsConnectionModalVisible)
        {
            mainVm.IsConnectionModalVisible = false;
            e.Handled = true;
        }
    }

    private void ClosePreviewModal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsFilePreviewModalVisible = false;
        }
    }

    private async void CopyPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(mainVm.FilePreviewContent);
            mainVm.EditorStatusMessage = "Preview contents copied to the clipboard.";
        }
        catch (Exception ex)
        {
            mainVm.EditorStatusMessage = $"Could not copy preview contents: {ex.Message}";
        }
    }

    private async void PreviewEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextEditor editor || e.Key != Key.C ||
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 ||
            string.IsNullOrEmpty(editor.SelectedText))
            return;

        e.Handled = true;
        await CopyPreviewSelectionAsync(editor);
    }

    private async void PreviewEditor_Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not TextEditor editor)
            return;

        await CopyPreviewSelectionAsync(editor);
    }

    private async Task CopyPreviewSelectionAsync(TextEditor editor)
    {
        if (string.IsNullOrEmpty(editor.SelectedText)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(editor.SelectedText);
            if (DataContext is MainViewModel mainVm)
                mainVm.EditorStatusMessage = "Selected preview contents copied to the clipboard.";
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel mainVm)
                mainVm.EditorStatusMessage = $"Could not copy the selected preview: {ex.Message}";
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
                // Parse an octal permission string (e.g. "755" -> 0o755).
                var text = tb.Text.Trim();
                if (text.Length is >= 3 and <= 4 && text.All(c => c is >= '0' and <= '7') &&
                    int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    var octalMode = 0;
                    var multiplier = 1;
                    foreach (var c in text.Reverse())
                    {
                        octalMode += (c - '0') * multiplier;
                        multiplier *= 8;
                    }

                    vm.ChangeFilePermissions(mainVm.SelectedFileForPermissions, (short)octalMode, mainVm);
                    mainVm.IsPermissionsModalVisible = false;
                    return;
                }

                vm.Status = $"Invalid permissions '{tb.Text}'. Use 3-4 octal digits (0-7), e.g. 755.";
                vm.StatusColor = "#f44336";
                return;
            }

            mainVm.SelectedFileForPermissions = null;
            mainVm.IsPermissionsModalVisible = false;
        }
    }

    private void CloseAboutDialog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsAboutDialogVisible = false;
        }
    }

    private void CloseUserManualDialog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsUserManualDialogVisible = false;
        }
    }

    private void OpenUserManualFromAbout_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.IsAboutDialogVisible = false;
            mainVm.IsUserManualDialogVisible = true;
        }
    }

}

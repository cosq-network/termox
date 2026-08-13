# Termox Features

This document consolidates the feature implementations for the Termox SSH client:
file management, advanced productivity, enterprise security, and network tools.
All features are implemented and additive; no breaking changes were introduced.

## Contents

- [File management](#file-management)
- [Advanced productivity](#advanced-productivity)
- [Enterprise features](#enterprise-features)
- [Network and SSH utilities](#network-and-ssh-utilities)
- [Technical specifications](#technical-specifications)
- [Testing](#testing)
- [Known limitations and future work](#known-limitations-and-future-work)
- [Files summary](#files-summary)

---

## File management

File operations UI, search/filtering, bookmarks, and session persistence.

### File operations UI & buttons

- **Upload File**: Opens the file picker for uploading files.
- **Download**: Downloads the selected file(s).
- **Delete**: Deletes files or folders, with confirmation.
- **Rename**: Renames files via a modal dialog.
- **Add Bookmark** (⭐): Saves the current directory as a bookmark.

Deletion supports recursive directory removal, real-time color-coded status
updates (red = error, green = success), and selection validation before
operations. The UI stays non-destructive and does not remove focus during
operations.

### Search & filtering

- Real-time, case-insensitive search box in the SFTP toolbar.
- `SearchQuery` property and `ApplyFilter()` in `SftpTabViewModel` maintain the
  full file list while displaying filtered results.
- Updates are immediate and require no network round-trip.

### Bookmarks / quick-access folders

- Save the current directory as a bookmark with the ⭐ button.
- Bookmarks appear in a dedicated sidebar section, each with a remove button.
- Persisted to `Termox/bookmarks.json` under the platform application-data
  directory and auto-loaded on startup.
- `ClearAllBookmarks()` supports batch removal.

### Session persistence & auto-reconnect

- Current connections auto-save when connecting or disconnecting.
- On startup, `LoadAndRestoreSessions()` reconnects to the last used sessions.
- Sessions store connection type (terminal/sftp), host, port, and username in
  `Termox/sessions.json`.
- Restoration is best-effort and gracefully handles missing profiles.

---

## Advanced productivity

Keyboard shortcuts, right-click context menus, file preview, and file
properties.

### Keyboard shortcuts

| Shortcut | Action | Context |
|----------|--------|---------|
| **F2** | Rename selected file | SFTP file list |
| **Ctrl+U** | Open upload dialog | SFTP file list |
| **Ctrl+D** | Download selected file(s) | SFTP file list |
| **Ctrl+Del** or **Delete** | Delete selected file/folder | SFTP file list |
| **Ctrl+S** | Add current path as bookmark | SFTP file list |

Shortcuts are captured via the `KeyDown` event on the SFTP list, run without
blocking the UI thread, and follow common application standards.

### Right-click context menu

Professional context menu with all file operations: Download, Upload, Rename,
Delete, Preview, Properties, and Add to Bookmarks. Options are context-aware
(enabled/disabled based on selection) and consistent with the application theme.

### File preview modal

Read-only text file preview for code (`.cs`, `.py`, `.js`, `.html`, `.css`,
`.sh`, `.bat`, `.cmd`), data (`.json`, `.xml`, `.yaml`, `.yml`, `.properties`,
`.conf`, `.cfg`), and documents (`.txt`, `.md`, `.log`).

- Monospace font, scrollable content area, full path in the title.
- Maximum file size of 1 MB prevents loading huge files.
- Graceful error handling with in-modal error display.
- Close button and ESC key support.

### File properties modal

Displays name, type, size (human-readable B/TB), modification timestamp,
Unix-style permissions (e.g., `-rw-r--r--`), and full remote path in a clean
tabular layout.

---

## Enterprise features

Password encryption, bulk file operations, and a file permissions editor.

### Password encryption

Secure credential storage using the platform credential store.

- **Windows**: DPAPI encryption for passwords at rest.
- **macOS**: Native Keychain storage.
- **Linux**: Secret Service storage through `secret-tool`.
- Passwords are never stored in plain text on disk; encrypted values carry an
  `ENCRYPTED:` prefix.
- `Services/CredentialManager.cs` exposes `EncryptCredential()`,
  `DecryptCredential()`, `IsEncrypted()`, `EncryptProfile()`, and
  `DecryptProfile()`.
- Legacy plaintext credentials are refused on load.

### Bulk file operations

- **Bulk Delete**: delete multiple selected files at once.
- Success/failure tracking with a summary ("Bulk delete: X deleted, Y failed.").
- Recursive directory deletion for nested structures.
- `Select All` / `Clear Selection` commands for mass selection.
- Color-coded status: green = success, orange = partial failures, red = errors.
- Async execution keeps the UI responsive; the "Bulk Del" button is disabled
  during transfers.

### File permissions editor

- Shows current permissions in Unix format (e.g., `-rw-r--r--`).
- Direct octal input (644, 755, 777, 600, ...).
- Quick presets:

| Preset | Usage | Octal |
|--------|-------|-------|
| 644 | Files (read-write owner, read others) | rw-r--r-- |
| 755 | Executables/directories (rwx owner, rx others) | rwxr-xr-x |
| 777 | Full access (all permissions) | rwxrwxrwx |
| 600 | Private files (owner only) | rw------- |

- Applies changes via SSH.NET `_sftpClient.ChangePermissions(path, mode)` in the
  background and refreshes the directory listing.

---

## Network and SSH utilities

The Tools tab provides five utilities for system administrators and developers.

### Port Scanner

TCP reachability checks for multiple ports (e.g., `22,80,443,3306`) against a
custom host. Uses `TcpClient.ConnectAsync()`, a configurable 2-second timeout,
a 100 ms delay between checks, and color-coded OPEN (green) / CLOSED (gray)
results.

### Ping Test

ICMP connectivity testing with a configurable ping count (1–10), response-time
measurement, and sequence numbering. Uses the .NET `Ping` class with a 5-second
per-ping timeout and 1-second delay between pings.

### SSH Key Generator

Generates RSA (1024–4096 bits) and ED25519 key pairs via SSH.NET, displays both
public and private keys, and copies them to the clipboard ready for
`authorized_keys`.

### Connection Batch Tester

Tests all saved SSH connection profiles sequentially with a 10-second timeout
per connection. Status indicators: SUCCESS (green), TIMEOUT (orange), FAILED
(red). Respects password and key-based authentication and reports response
times.

### Speed Test

Framework for benchmarking upload/download speeds over SFTP with configurable
test size (1–100 MB). Currently a placeholder ready for integration with active
SFTP sessions.

### Tools tab integration

The Tools tab appears as the fifth tab, is opened via an "Open Tools" button in
the Sessions sidebar, preserves its state during the session, and can be closed.
The Connection Tester reuses saved profiles from `MainViewModel`.

---

## Technical specifications

### Architecture

- **Design pattern**: MVVM (Model-View-ViewModel)
- **UI framework**: Avalonia 12.1.0
- **SSH library**: Renci.SshNet (SSH.NET v2025.1.0)
- **Framework**: .NET 10.0
- **Data persistence**: JSON serialization to the platform application-data
  directory under `Termox/`
- **Threading**: Async operations for non-blocking UI

### Data storage locations

| Data | Location |
| --- | --- |
| Connections | `Termox/connections.json` |
| Bookmarks | `Termox/bookmarks.json` |
| Sessions | `Termox/sessions.json` |

### Credential flow

```text
SaveConnection()
  └─> CredentialManager.EncryptProfile()
       └─> JsonSerializer.Serialize(encrypted)

LoadConnections()
  ├─> JsonSerializer.Deserialize()
  └─> CredentialManager.DecryptProfile()
```

### Utility performance

| Utility | Timing |
| --- | --- |
| Port scan | ~2 seconds per port (configurable) |
| Ping | ~1 second per attempt + network latency |
| Key generation | <1 second |
| Connection test | Up to 10 seconds per connection |
| Speed test | Depends on network speed |

### Compatibility

- **Platforms**: Windows (full DPAPI support), macOS (Keychain), Linux
  (Secret Service provider required).
- **Protocols**: TCP (port scanning), ICMP (ping), SSH (connection testing),
  SFTP (speed testing framework).
- **SSH versions**: Compatible with SSH2 via SSH.NET.

---

## Testing

### Build status

- **Debug build**: ✅ 0 warnings, 0 errors
- **Release build**: ✅ 0 warnings, 0 errors
- **Automated tests**: 8 passing unit tests in `tests/Termox.Tests`
- **CI/CD**: GitHub Actions validation and release workflows

### Feature verification

- [x] File upload/download/delete (single and directory) works through the UI
- [x] File renaming works with modal dialog
- [x] Search/filter updates in real-time
- [x] Bookmarks save, persist, and remove across restarts
- [x] Sessions auto-save on connect and restore on startup
- [x] Keyboard shortcuts work correctly (F2, Ctrl+U, Ctrl+D, Ctrl+Del, Ctrl+S)
- [x] Context menu appears on right-click with functional items
- [x] File preview opens for text files; large files (>1 MB) handled gracefully
- [x] File properties modal displays all metadata
- [x] Password encryption on save / decryption on load
- [x] Bulk delete with success/failure tracking
- [x] Permissions editor modal, presets, and octal input work
- [x] Port Scanner identifies open/closed ports
- [x] Ping Test measures latency
- [x] SSH Key Generator supports RSA/ED25519
- [x] Connection Batch Tester tests all profiles
- [x] UI remains responsive during operations; no threading issues
- [x] Project builds without errors or warnings
- [x] Automated security and path-safety tests pass
- [x] Release workflow YAML and packaging scripts validate

---

## Known limitations and future work

### Current limitations

- **Drag & drop** upload is not implemented (Avalonia `IDataObject` API
  complexity); placeholder handlers exist.
- **File preview** is read-only, limited to text files under 1 MB, and has no
  syntax highlighting.
- **File permissions** cannot be modified from the Properties dialog.
- **Bulk operations** currently cover only delete (rename/chmod can be added).
- **Speed Test** is a framework placeholder until it can use an active SFTP
  session.
- **Permission presets** are limited to 4 common patterns.
- **Linux** requires an available Secret Service provider for credential
  storage.

### Future enhancements

- Drag-and-drop file uploads to the SFTP window
- Batch file operations (rename, chmod patterns)
- Search and replace in file preview; syntax highlighting
- Binary file preview (hex dump)
- File comparison tool
- Visual permission calculator editor
- Undo/redo for file operations
- Operation history/audit log
- SSH config parser (`~/.ssh/config` auto-population)
- Network analyzer and real-time bandwidth monitoring
- Command runner for custom SSH commands
- Certificate checker (SSL/TLS validity)
- Firewall rules display

---

## Files summary

| File | Type | Purpose |
| --- | --- | --- |
| `Services/CredentialManager.cs` | New | Password encryption/decryption |
| `ViewModels/ToolsTabViewModel.cs` | New | Network utility implementations |
| `ViewModels/MainViewModel.cs` | Modified | Session management, bookmarks, modals, credential integration |
| `ViewModels/SftpTabViewModel.cs` | Modified | File operations, search, preview, bulk ops, permissions |
| `Models/RemoteFileModel.cs` | Modified | Added `IsSelected` property |
| `Views/MainWindow.axaml` | Modified | Toolbar, buttons, modals, context menu, Tools tab UI |
| `Views/MainWindow.axaml.cs` | Modified | Event handlers, keyboard shortcuts, dialogs |

All changes are additive and backward-compatible. Release and repository setup
instructions are documented in [CI-CD-INTEGRATION.md](CI-CD-INTEGRATION.md).

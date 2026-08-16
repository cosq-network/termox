# Termox - Suggested Minor Improvements

## 1. **Quick Tools Access - Keyboard Shortcuts**

Add keyboard shortcuts for frequently-used tools to improve workflow speed:

```
Ctrl+1          → Open/Focus Port Scanner
Ctrl+2          → Open/Focus Ping Test
Ctrl+3          → Open/Focus SSH Key Generator
Ctrl+4          → Open/Focus Connection Tester
Ctrl+Shift+T    → Open Tools Tab
Ctrl+Shift+D    → Open/Focus DNS Inspector
Ctrl+Shift+G    → Open/Focus GPG Manager
Ctrl+Shift+F    → Open/Focus Fingerprint Utilities
```

**Implementation**: Add KeyDown event handlers in MainWindow.axaml.cs to wire these shortcuts to commands.

---

## 2. **Search Across All Saved Sessions**

Currently, sessions are displayed in a scrollable list with no search capability.

**Improvement**:
- Add a search/filter textbox above the Sessions list
- Filter by profile name, host, or username
- Real-time filtering as user types
- Shows "X of Y sessions" counter

**Files to modify**: MainWindow.axaml (add TextBox), MainViewModel.cs (add FilteredSessions property)

---

## 3. **Recently Used Sessions**

Add a "Recent" section at the top of the Sessions tab showing last 5-10 used connections.

**Benefits**:
- Faster access to frequently-used servers
- No need to scroll through full session list
- Reduces friction for common workflows

**Implementation**:
- Track last-used timestamp when connecting
- Sort by recency
- Store in session metadata

---

## 4. **Connection Profile Duplication**

Add "Duplicate Profile" option to quickly clone existing profiles with minor changes.

**Implementation**:
- Right-click context menu on saved sessions
- Creates copy with "(copy)" suffix in name
- User can edit and save
- Faster than re-entering all details

---

## 5. **Batch Bookmark Operations**

Allow selecting multiple bookmarks for bulk operations.

**Current**: Delete one bookmark at a time with confirmation modal

**Improvement**:
- Multi-select bookmarks (Ctrl+Click, Shift+Click)
- "Delete Selected" button for batch delete with single confirmation
- "Open All in New Tabs" option to quickly open multiple sessions

---

## 6. **Export/Import Sessions and Bookmarks**

Allow users to backup and share their session configurations.

**Features**:
- Export to JSON or encrypted format
- Import previously exported configurations
- Option to merge or replace existing
- Useful for team sharing or backup

**Implementation**: Add "Import"/"Export" buttons in Sessions/Bookmarks tabs

---

## 7. **Tab Color Tags**

Add optional color tags to terminal/SFTP tabs for visual organization.

**Benefit**: Quickly identify tabs by environment (dev/staging/prod)

**Implementation**:
- Right-click tab → "Set Color Tag"
- Store in session metadata
- Show colored dot on tab title

---

## 8. **Quick Copy Buttons for Connection Details**

Add one-click copy buttons for each connection field.

**Current**: User must manually select and copy hostname, port, etc.

**Improvement**:
- Small copy icons next to Host, Port, Username fields in Sessions list
- Copies to clipboard instantly
- Useful for documentation or sharing with colleagues

---

## 9. **Tool Result History/Log**

Add a persistent log of recent tool queries (DNS, Port Scan, Ping results).

**Implementation**:
- New "History" tab showing last 20 queries
- Each entry shows: timestamp, tool name, input, result
- Click to re-run same query
- Export history to CSV

---

## 10. **Favorites/Pinning for Bookmarks**

Allow marking bookmarks as "favorites" to keep them at top of list.

**Implementation**:
- Star/pin icon on each bookmark
- Pinned bookmarks appear first
- Toggle with right-click or keyboard shortcut

---

## 11. **Connection Profile Tags**

Add optional tags to connection profiles for better organization.

**Example tags**: `production`, `development`, `staging`, `backup`, `client-xyz`

**Benefits**:
- Filter sessions by tag
- Color-code tabs by tag
- Quick visual identification of environment
- Better for teams managing many servers

---

## 12. **Improved Status Bar Information**

Enhance the status bar to show more useful information.

**Current**: "Ready" status only

**Suggestions**:
- Show active connection count
- Show current SFTP directory path
- Show transfer speed during downloads
- Show selected file count in SFTP

---

## 13. **SSH Agent Integration**

Add support for SSH agent to unlock stored keys.

**Benefits**:
- Avoid re-entering passphrases for each key
- Follow SSH best practices
- Improve security posture

---

## 14. **Preview Improvements for Text Files**

Enhance the file preview feature.

**Current**: Read-only preview, limited to supported text formats

**Improvements**:
- Add syntax highlighting for code files (.py, .js, .sh, .conf, etc.)
- Add line numbers
- Search within preview (Ctrl+F)
- Adjustable font size
- Support for more formats (JSON, YAML, XML)

---

## 15. **Drag & Drop SFTP Upload (Already noted as limitation)**

Implement drag-and-drop file upload from local system.

**Benefit**: Much faster than file picker for multiple files

**Implementation Challenge**: Requires terminal control integration in Avalonia

---

## 16. **Terminal Clear History Button**

Add a button or keyboard shortcut to clear terminal history.

**Shortcut suggestion**: `Ctrl+L` (Unix convention)

**Implementation**: Add command to terminal rendering library

---

## 17. **Automatic Connection Retry on Timeout**

Add retry logic for failed connections with exponential backoff.

**Configuration**:
- Number of retries (default: 3)
- Retry delay (default: 2 seconds)
- Show retry count in connection dialog

---

## 18. **Quick Connect Dialog**

Add a global "Quick Connect" modal for one-off SSH connections without saving.

**Keyboard**: `Ctrl+Q`

**Fields**: Host, Port, Username, Password

**Benefit**: Fast access to ad-hoc servers

---

## 19. **SFTP Favorites**

Add "star" icon to favorite frequently-accessed SFTP directories.

**Benefits**:
- Quick navigation bookmarking within SFTP
- Shows in dropdown for fast access
- Different from global Bookmarks feature

---

## 20. **File Size Warnings Before Download**

Add warning dialog before downloading files larger than configurable size.

**Implementation**:
- Configuration option for size threshold (default: 100MB)
- Show file size and estimated download time
- Ask for confirmation
- Prevent accidental large file downloads

---

## Priority Ranking (Easiest to Hardest)

### Quick Wins (1-2 hours)
1. Keyboard shortcuts for tools (#1)
2. Quick copy buttons for connection details (#8)
3. Search across sessions (#2)
4. Terminal clear history (#16)

### Medium Effort (2-4 hours)
5. Session/Bookmark tags (#11)
6. Favorites/pinning (#10)
7. Export/Import sessions (#6)
8. Connection profile duplication (#4)

### More Involved (4+ hours)
9. Tab color tags (#7)
10. Tool result history (#9)
11. Preview improvements (#14)
12. Drag & drop upload (#15)
13. SSH agent integration (#13)
14. Quick connect dialog (#18)

---

## Recommended Implementation Order

1. **Start with #1 (Keyboard shortcuts)** - High value, relatively simple
2. **Then #2 (Search sessions)** - Commonly requested feature
3. **Then #8 (Copy buttons)** - Quick win, improves UX
4. **Then #11 (Tags)** - Useful organizational feature
5. **Then #10 (Favorites)** - Complements tags

---

## Technical Notes

- Most features integrate with existing architecture without major refactoring
- Keyboard shortcuts use Avalonia's InputBindings pattern
- Session/Bookmark metadata can use existing JSON storage
- Consider adding Settings/Preferences dialog for user-configurable options (threshold sizes, retry counts, etc.)

# 03 — SFTP File Operations

Covers browse, upload/download, delete, rename, permissions, preview, and
edit. This section is the most affected by the concurrency hardening (operation
gate, dispose race, navigation no-op) and the symlink-cycle guards, so the
stress tests below matter.

## 3.1 Browse and navigate

- [ ] **SFTP-01** Open an SFTP tab — the directory listing loads with
      folders first, then files, sorted by name.
- [ ] **SFTP-02** Double-click into `~/termox-test/subdir` and navigate back
      up with the Up button.
- [ ] **SFTP-03** Type a path into the path box and press Enter — navigates.
- [ ] **SFTP-04** **Regression check (navigation no-op)**: start a large
      download, then immediately click into another directory — the listing
      **eventually** refreshes (does not silently stay on the old directory).
- [ ] **SFTP-05** Search box filters the listing in real time, case-insensitive.

## 3.2 Upload / download

- [ ] **XFER-01** Upload a small text file — status shows progress, then
      "Uploaded 1 items successfully!" (green).
- [ ] **XFER-02** Upload a directory (multi-file picker) — all files land on
      the remote side.
- [ ] **XFER-03** Download a small file to a local folder — succeeds.
- [ ] **XFER-04** Download a file that already exists locally — it is skipped
      with a log message, no overwrite.
- [ ] **XFER-05** **Size warning**: download a selection larger than the 100 MB
      threshold (or lower the threshold temporarily in
      `MainViewModel.DownloadSizeWarningThreshold` and rebuild) — the warning
      modal lists the largest items; Cancel aborts; Continue proceeds.
- [ ] **XFER-06** **Cancel**: start a large download, click Cancel — the
      partial local file is removed, status shows "Download Cancelled."
- [ ] **XFER-07** **Cancel (upload)**: start a large upload, click Cancel —
      status shows "Upload Cancelled." and the partial remote file is removed.
- [ ] **XFER-08** **Pause/Resume**: during a download, Pause freezes progress;
      Resume continues.
- [ ] **XFER-09** **Symlink cycle guard**: start a download of
      `~/termox-test` (which contains `subdir-link` → `subdir`) — the download
      completes without infinite recursion or hang, and the local copy does not
      escape the destination folder.
- [ ] **XFER-10** **Symlink traversal guard**: attempt to download a remote
      path that symlinks outside the remote tree — no file is written outside
      the chosen local destination.

## 3.3 Delete

- [ ] **DEL-01** Delete a single file with confirmation — removed, listing
      refreshes, status green.
- [ ] **DEL-02** Bulk-select several files and delete — summary
      "Bulk delete: N deleted, 0 failed."
- [ ] **DEL-03** Delete a directory recursively — nested contents removed.
- [ ] **DEL-04** **Symlink cycle guard**: delete a directory tree containing
      `subdir-link` — completes without infinite recursion.
- [ ] **DEL-05** Delete a file during an active transfer (different selection)
      — no crash; operations are serialized.
- [ ] **DEL-06** Close the SFTP tab while a bulk delete is running — no crash,
      no `ObjectDisposedException` in the log.

## 3.4 Rename

- [ ] **RN-01** F2 on a selected file — rename modal pre-filled with the
      current name.
- [ ] **RN-02** Rename to a valid name — success, listing refreshes.
- [ ] **RN-03** Rename to a name containing `/`, `\`, or `..` — rejected with a
      clear error; the modal closes without crashing.
- [ ] **RN-04** Empty rename — cancelled cleanly.

## 3.5 Permissions

- [ ] **PERM-01** Select a file, open Permissions — the modal shows the current
      Unix mode.
- [ ] **PERM-02** Apply preset `755` — succeeds, listing refreshes, status
      shows the new octal value.
- [ ] **PERM-03** Type `777` and Apply — sets `rwxrwxrwx`.
- [ ] **PERM-04** **Regression (octal validation)**: type `999` and Apply —
      an error is shown ("Invalid permissions…"), the modal stays open, and
      the file is **unchanged**.
- [ ] **PERM-05** Type `abc` and Apply — same error behavior, no silent close.
- [ ] **PERM-06** Type `7555` (4-digit, includes sticky/setuid bits) and Apply
      — accepted and applied.
- [ ] **PERM-07** Cancel — modal closes, nothing changes.

## 3.6 Preview

- [ ] **PV-01** Right-click a `.txt` file → Preview — the modal opens with
      line numbers, read-only editor, and a header showing name + line/char
      count + "Read-only".
- [ ] **PV-02** Ctrl/Cmd+C with a selection copies the selection; **Copy All**
      copies the full content; the status bar confirms.
- [ ] **PV-03** ESC closes the preview.
- [ ] **PV-04** Preview the 20 MB+ file (`25mb.bin`) — an error message is
      shown in the modal ("File exceeds the 20MB preview limit"), no hang.
- [ ] **PV-05** Preview a directory — "Cannot preview a directory."
- [ ] **PV-06** Open Preview on a file, then close the SFTP tab — the modal
      either stays usable or closes; no crash.

## 3.7 Edit

- [ ] **EDIT-01** Right-click a `.txt` file → Edit — an editor tab opens,
      loads the content.
- [ ] **EDIT-02** Modify and Save — "Saved …" green; the remote file reflects
      the change.
- [ ] **EDIT-03** Modify, then close the tab — the "Save changes?" confirm
      appears; Discard / Save-and-close both work.
- [ ] **EDIT-04** **Stale-file guard**: open a file for edit, change it on the
      server (e.g. `echo x >> file` in a terminal), then Save — the save is
      refused with a "remote file changed" error.
- [ ] **EDIT-05** Edit the 20 MB+ file — "larger than the 20MB editor limit"
      error, no hang.

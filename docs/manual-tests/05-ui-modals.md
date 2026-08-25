# 05 — UI, Modals, and Keyboard Shortcuts

Covers modal behavior (including the new Escape handling and host-key modal),
keyboard shortcuts (including the macOS Cmd fix), and the file-properties
display fix.

## 5.1 Modal behavior

- [ ] **MODAL-01** **Escape**: open the **connection modal**, press Escape — it
      closes.
- [ ] **MODAL-02** **Escape**: open the **file preview**, press Escape — it
      closes.
- [ ] **MODAL-03** **Escape**: open the **permissions modal**, press Escape —
      it closes without applying.
- [ ] **MODAL-04** **Escape**: open the **properties modal**, press Escape — it
      closes.
- [ ] **MODAL-05** **Escape**: open the **rename modal**, press Escape — it
      closes and clears the draft name.
- [ ] **MODAL-06** **Escape**: open the **host-key verification modal**, press
      Escape — treated as Reject; the connection is refused.
- [ ] **MODAL-07** Each modal blocks interaction with the window behind it
      (overlay covers the app).
- [ ] **MODAL-08** The ✕ button and any Cancel buttons close their modal
      without side effects.

## 5.2 Keyboard shortcuts (SFTP file list)

- [ ] **KEY-01** **F2** opens rename for the selected file.
- [ ] **KEY-02** **Ctrl+U** opens the upload picker.
- [ ] **KEY-03** **Ctrl+D** downloads the selection.
- [ ] **KEY-04** **Ctrl+S** adds the current directory to bookmarks.
- [ ] **KEY-05** **Delete** and **Ctrl+Delete** delete the selection.
- [ ] **KEY-06** **macOS regression**: repeat KEY-02..04 with **Cmd** instead
      of Ctrl — they work (the shortcut no longer requires Ctrl).

## 5.3 File properties modal

- [ ] **PROP-01** Select a file → Properties — **Type** shows **"File"** (not
      "True"/blank).
- [ ] **PROP-02** Select a directory → Properties — **Type** shows
      **"Directory"**.
- [ ] **PROP-03** Size, Modified, Permissions, and Path fields are populated
      correctly.
- [ ] **PROP-04** A symbolic link → Type shows "Symbolic Link".

## 5.4 Window and layout

- [ ] **UI-01** The app opens maximized/normal without clipping; the sidebar
      and tab strips render.
- [ ] **UI-02** Dark theme is consistent (no glaring light backgrounds).
- [ ] **UI-03** Resize the window to a small size — the layout degrades
      gracefully (scrollbars appear, no unreachable controls).
- [ ] **UI-04** On a HiDPI display, text and icons are crisp.

## 5.5 General stability

- [ ] **STAB-01** Open/close 5+ tabs rapidly (mix of terminal and SFTP) — no
      crash, no unhandled exceptions in the log.
- [ ] **STAB-02** Open a terminal tab, an SFTP tab, an editor tab, and the
      Tools tab simultaneously, then close the window — clean shutdown.
- [ ] **STAB-03** Run the app for 30 minutes with an idle terminal connected —
      no crash, no memory growth visible in the task manager/Activity Monitor.

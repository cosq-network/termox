# 02 — Terminal and Sessions

Covers terminal connect/reconnect, keep-alive, idle timeout, and session
restore. This section exercises the terminal `Reconnect` settings-preservation
and input-locking fixes.

## 2.1 Terminal basics

- [ ] **TERM-01** Connect a terminal tab to the test server — prompt appears,
      ANSI colors render correctly, typing echoes.
- [ ] **TERM-02** Run a few commands (`ls`, `echo`, `top` in a paused state) —
      output streams live, no truncation.
- [ ] **TERM-03** Resize the window — the remote terminal reflows (no
      horizontal scroll artifacts).
- [ ] **TERM-04** Ctrl+L clears the terminal buffer.
- [ ] **TERM-05** Close the tab with the ✕ — the tab closes, the session
      disconnects, no error in the terminal.

## 2.2 Keep-alive and idle timeout

- [ ] **KEEP-01** Connect with keep-alive **60 s** and idle timeout **0**
      (default). Leave the session idle for 2+ minutes — the session stays
      connected (no drop through NAT).
- [ ] **KEEP-02** Connect with idle timeout set to **1 minute**. Wait — a
      warning appears in the terminal ~60 s before disconnect ("Session will
      disconnect in ~Xs unless you interact").
- [ ] **KEEP-03** After the warning, **type a character** — the session stays
      connected (warning is not repeated for a while).
- [ ] **KEEP-04** Connect with idle timeout **1 minute** and do nothing — the
      session disconnects with the idle-timeout reason, and the **Reconnect**
      button becomes available.
- [ ] **KEEP-05** **Regression check**: configure keep-alive 60 s + idle
      timeout 2 min, connect, then click **Reconnect** — the settings are
      preserved (keep-alive still sends, idle timeout still applies). This
      verifies the reconnect-settings fix.

## 2.3 Reconnect

- [ ] **RECONN-01** From a disconnected session, click **Reconnect** — it
      reconnects with the same host/user/key settings.
- [ ] **RECONN-02** Reconnect after a host-key verified profile — **no**
      host-key prompt on reconnect (fingerprint already known).

## 2.4 Session restore

- [ ] **RESTORE-01** Open a terminal tab and an SFTP tab, then close Termox
      normally (✕ on window).
- [ ] **RESTORE-02** Relaunch Termox — both tabs restore automatically
      (terminal reconnects; SFTP reconnects at the same directory).
- [ ] **RESTORE-03** Delete a saved profile that a restored session references,
      relaunch — the app starts cleanly, the orphaned session is skipped
      without crashing.
- [ ] **RESTORE-04** While Termox is closed, hand-edit `sessions.json` so an
      SFTP `RemotePath` contains `; echo pwned; #` — relaunch. The SFTP tab
      must **not** execute anything; it opens at a safe path or the home
      directory.

## 2.5 Shutdown

- [ ] **SHUT-01** With an SFTP transfer running, close the main window — the
      app shuts down cleanly (no crash, no hung process).
- [ ] **SHUT-02** Relaunch immediately after a forced close (kill the process)
      — no corrupted `connections.json`/`sessions.json`; the app starts.

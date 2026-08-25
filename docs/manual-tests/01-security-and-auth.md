# 01 — Security and Authentication

Covers host-key verification, authentication methods, credential storage, and
the connection test. This is the most important section: the host-key flow
changed in this hardening pass.

## 1.1 Host-key verification (first connect)

Prerequisites: a profile **without** a stored fingerprint (delete the saved
profile or use a fresh host), and a server you control.

- [ ] **HOST-01** Open the connection modal for a brand-new host and click
      **Test Connection**.
- [ ] **HOST-02** The **"Verify Host Key"** modal appears showing the host
      name and a `SHA256:...` fingerprint before the connection completes.
- [ ] **HOST-03** The connection is **refused** until you act (status stays
      "Testing..." / no success until a choice is made).
- [ ] **HOST-04** Click **Reject** — the test reports failure and the
      fingerprint is **not** saved. Re-running the test prompts again.
- [ ] **HOST-05** Click **Test Connection** again, then **Trust This Host** —
      the test succeeds and the fingerprint is persisted to the profile.
- [ ] **HOST-06** Reconnect to the same host — **no** prompt appears (known
      host), the connection proceeds directly.
- [ ] **HOST-07** (requires a second server or host-key rotation) Connect to a
      host whose key does **not** match the stored fingerprint — the
      connection is refused. (You can simulate by editing the stored
      fingerprint in `connections.json` to a wrong value and reconnecting.)

## 1.2 Authentication methods

- [ ] **AUTH-01** Password auth: create a profile with a password, connect a
      terminal tab. Prompt: none; login succeeds.
- [ ] **AUTH-02** Key auth (unencrypted key): profile with private key path
      and **no passphrase**, no password. Terminal + SFTP connect succeed.
- [ ] **AUTH-03** Key auth (encrypted key): profile with a passphrase-protected
      key. Enter the correct passphrase in **Private Key Passphrase** — connect
      succeeds.
- [ ] **AUTH-04** Wrong passphrase on an encrypted key — connect fails with a
      clear error (not a password error).
- [ ] **AUTH-05** **Regression check**: a profile that previously relied on the
      account password decrypting the key now requires the passphrase field.
      Verify the error message is understandable if the passphrase is missing.
- [ ] **AUTH-06** The **password** field and **passphrase** field both mask
      input with `•`; the values are not visible in plain text.
- [ ] **AUTH-07** Save a profile with both password and passphrase; close and
      reopen Termox; the profile loads and both fields decrypt correctly
      (terminal + SFTP connect without re-entering).

## 1.3 Connection test (modal)

- [ ] **TEST-01** Valid host/user/port + correct credentials → **"Test
      Successful!"**, green.
- [ ] **TEST-02** Wrong password → failure with a meaningful message, red.
- [ ] **TEST-03** Unreachable host/port (e.g. a firewalled IP) → the test
      returns within ~10 s with a timeout message; the UI stays responsive.
- [ ] **TEST-04** Click **Test Connection** twice quickly — only one test
      runs; the UI does not stack connections.
- [ ] **TEST-05** Save is disabled until a test passes (`IsTestSuccessful`).

## 1.4 Credential storage

- [ ] **CRED-01** After saving profiles, `connections.json` under the
      platform app-data `Termox/` directory contains **no plaintext**
      passwords or passphrases (values are `ENCRYPTED:`/`KEYCHAIN:` prefixed).
- [ ] **CRED-02** `sessions.json` and `bookmarks.json` contain **no**
      passwords or passphrases.
- [ ] **CRED-03** Legacy plaintext credentials are refused on load (if you
      have a pre-encryption profile, verify it is not loaded silently).
- [ ] **CRED-04** Tamper with `sessions.json` `RemotePath` to include a
      control character or `;` and restart — the SFTP session either ignores
      the path or opens at the home directory; **no shell command executes**.

## 1.5 Bookmarks and shell command safety

- [ ] **BM-01** Add a bookmark at `~/termox-test/subdir` and open it in a
      terminal — the terminal `cd`s to the directory.
- [ ] **BM-02** Open the same bookmark in SFTP — it opens at the path.
- [ ] **BM-03** Try to create a bookmark whose path contains `;`, `` ` ``, `$(`,
      or a newline (e.g. by editing `bookmarks.json`) and open it in a
      terminal — nothing is executed; the command is refused.

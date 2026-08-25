# 04 — Tools and Diagnostics

Covers the Tools tab utilities. The subprocess timeouts (dig/nslookup, gpg)
are the key changes to verify here — a hung system tool must not freeze the app.

## 4.1 Port Scanner

- [ ] **TOOL-01** Scan `localhost` for ports `22,80,443` — OPEN/CLOSED results
      color-coded, responsive UI.
- [ ] **TOOL-02** **CanExecute regression**: click **Scan** twice quickly —
      the second click is ignored while a scan is running (button disabled).
- [ ] **TOOL-03** Scan an unreachable host — results complete, UI stays
      responsive.

## 4.2 Ping Test

- [ ] **TOOL-04** Ping `localhost` with count 4 — sequence numbers and response
      times shown.
- [ ] **TOOL-05** Ping an unreachable IP — timeout entries appear, the tool
      finishes without hanging.

## 4.3 SSH Key Generator

- [ ] **TOOL-06** Generate an RSA 2048 key — public and private key display;
      copy to clipboard works.
- [ ] **TOOL-07** Generate an ED25519 key — works.
- [ ] **TOOL-08** Connect to a server using a generated key pasted into
      `authorized_keys` (optional, if you have server access).

## 4.4 Connection Tester

- [ ] **TOOL-09** Run the batch tester over 2-3 saved profiles — each reports
      SUCCESS/TIMEOUT/FAILED with response time.
- [ ] **TOOL-10** Include a profile with an encrypted key + passphrase — the
      tester respects the stored passphrase (no spurious auth failure).

## 4.5 Server Stats

- [ ] **TOOL-11** Collect stats for a saved profile — CPU, memory, top
      processes, and disk volumes render; summary cards color-coded.
- [ ] **TOOL-12** **CanExecute regression**: click **Collect** twice — the
      second is ignored while collecting (button disabled).
- [ ] **TOOL-13** Sort by CPU / memory / disk — the tables reorder.
- [ ] **TOOL-14** Close the Server Stats tab while collecting — no crash.

## 4.6 DNS Inspector

- [ ] **TOOL-15** Query A records for a known domain — results populate.
- [ ] **TOOL-16** Query-all for a domain — all record types populate without
      hanging.
- [ ] **TOOL-17** **Timeout regression**: query a domain that resolves slowly
      (or unplug the network) — the query returns an error within ~15 s; the
      UI never freezes.

## 4.7 GPG Key Manager

- [ ] **TOOL-18** List public keys — the keyring populates.
- [ ] **TOOL-19** Import a test public key — success message.
- [ ] **TOOL-20** Export a public key — armored output displays.
- [ ] **TOOL-21** Delete an imported test key — success.
- [ ] **TOOL-22** **Timeout regression**: make `gpg` unavailable/hung (e.g.
      rename the binary temporarily) — the operation returns an error within
      ~30 s instead of hanging the app.

## 4.8 Fingerprint Utilities

- [ ] **TOOL-23** Compute a SHA-256 fingerprint for text and for a file —
      formatted consistently.
- [ ] **TOOL-24** Compare two fingerprints (with/without colons/spaces) —
      reported as matching.

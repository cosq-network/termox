# Termox Manual Test Suite — Pre-Release

This suite is the manual verification checklist to complete before cutting a
Termox release. It focuses on the areas changed by the security and stability
hardening work (host-key verification, key passphrases, SFTP concurrency,
permissions validation, modal behavior) plus the core flows that must never
regress.

## How to use this suite

1. Work through the sections **in order** — earlier sections build up state
   (saved profiles, known hosts) that later sections depend on.
2. Tick each box `[ ]` → `[x]` as you verify it, and note the build/version
   under test in the sign-off section.
3. If any box fails, record the failure in the section and stop before
   signing off — do not proceed to release.
4. The suite assumes a test SSH server you control. Use a disposable server or
   container if possible; the suite deletes files and directories.

## Sections

| Document | Scope |
| --- | --- |
| [01-security-and-auth.md](01-security-and-auth.md) | Host-key verification, password/key/passphrase auth, credential storage, connection testing |
| [02-terminal-sessions.md](02-terminal-sessions.md) | Terminal connect/reconnect, keep-alive, idle timeout, session restore |
| [03-sftp-file-operations.md](03-sftp-file-operations.md) | Browse, upload/download, delete, rename, permissions, preview, edit, concurrency |
| [04-tools-diagnostics.md](04-tools-diagnostics.md) | Port scanner, ping, key generator, connection tester, server stats, DNS, GPG |
| [05-ui-modals.md](05-ui-modals.md) | Modal behavior, keyboard shortcuts, Escape handling, focus, properties |

## Environment

| Item | Value |
| --- | --- |
| OS under test | Windows 11 / macOS (Intel + Apple Silicon) / Linux |
| .NET SDK | 10.0 |
| Build | `dotnet build Termox.csproj --configuration Release` |
| Test server | SSH server with a user account, reachable hostname/IP |
| Key files | An encrypted RSA key (`id_ed25519`/`id_rsa` with passphrase) and an unencrypted one |
| System tools | `ssh-keygen`, `dig` (macOS/Linux), `nslookup`, `gpg`, `ping` available |

### Test server prep

On the server, prepare a scratch area:

```bash
mkdir -p ~/termox-test/{subdir,empty,large}
echo "hello termox" > ~/termox-test/hello.txt
head -c 25M /dev/urandom > ~/termox-test/large/25mb.bin
ln -s ~/termox-test/subdir ~/termox-test/subdir-link   # symlinked directory
```

A 20 MB+ file (`25mb.bin`) is required to verify the preview/edit size limit.

## Sign-off

| Item | Value |
| --- | --- |
| Build under test | (e.g. `57e562d` / v1.x.x) |
| OS / architecture | |
| Tested by | |
| Date | |
| Result | PASS / FAIL |

If FAIL, list the failing test IDs here and do not release until they are
resolved and the suite is re-run.

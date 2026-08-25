# Termox

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue.svg)

Termox is a cross-platform SSH terminal and SFTP client for managing remote
servers from one desktop application. It combines interactive terminal
sessions, remote file management, saved connection profiles, bookmarks, and
network diagnostics in a focused Avalonia UI.

Termox is developed and maintained by COSQ NETWORK PRIVATE LIMITED.

## Features

### SSH terminal

- Open SSH connections in separate terminal tabs.
- Save connection profiles with a name, host, port, username, password,
  optional private-key path, and optional private-key passphrase.
- Support password and private-key authentication through SSH.NET, with a
  distinct passphrase for encrypted private keys (never the account password).
- Test connection details before saving a profile.
- Restore active terminal and SFTP sessions when the application starts.
- Open a terminal from a saved session or a bookmark.
- Use terminal context-menu actions to open SFTP at the current remote
  directory or add that directory to bookmarks.
- Display terminal output with ANSI color support and a dark theme.
- Keep long-idle sessions alive with a configurable SSH keep-alive interval
  (60 s by default), so NAT, proxy, and `sshd` timeouts do not drop the
  session.
- Configure an optional idle timeout that warns in the terminal about 60
  seconds before disconnecting, giving you a chance to interact; set to
  `0` to keep sessions open indefinitely (the default).
- Retry failed connections with a configurable retry count and delay,
  shown in the connection dialog and persisted per profile.
- Clear the terminal buffer with Ctrl+L (Cmd+L on macOS).

### SFTP browser

- Browse the remote filesystem and navigate through directories.
- Keep SFTP tabs separate from terminal tabs.
- Open SFTP from a saved session or bookmark at a selected path.
- Upload files through the file picker.
- Download selected files.
- Warn before downloading large selections: when the combined size exceeds
  the configured threshold (100 MB by default), a confirmation dialog
  lists the largest items before the transfer starts.
- Rename files and directories.
- Delete one selected item with confirmation.
- Delete multiple selected items with bulk-delete confirmation.
- Edit Unix file permissions.
- Show file name, type, size, modification time, permissions, and full path.
- Preview supported text files without downloading them first.
- Filter the current directory listing with case-insensitive search.
- Refresh the current directory and navigate to its parent.
- Use toolbar icons, keyboard shortcuts, and right-click context menus for file
  operations.

Drag-and-drop upload and in-place text editing are not currently available.
File preview is read-only and limited to supported text files.

### Sessions and bookmarks

- Store saved sessions in a dedicated Sessions tab.
- Show a Recently Used list of the ten most recently opened sessions, with
  one-click access to open them as terminal or SFTP.
- Store bookmarks in a dedicated Bookmarks tab.
- Associate each bookmark with its saved session and remote path.
- Pin bookmarks as favorites with the star icon, or from the right-click
  menu; favorites appear in a dedicated Favorites section at the top of the
  Bookmarks tab.
- Open a bookmark in either a new SSH terminal or a new SFTP tab.
- Delete bookmarks with confirmation.
- Persist sessions and bookmarks between application runs.

### Network and SSH tools

The Tools tab provides the following utilities:

- Port Scanner: test TCP reachability for a host and a list of ports.
- Ping Test: send ICMP requests and display response times.
- SSH Key Generator: create RSA or ED25519 key pairs using the system
  `ssh-keygen` command and copy the generated keys.
- Connection Tester: test all saved SSH connection profiles and report status
  and response time.
- SSH Endpoint Test: provide an endpoint testing interface for SSH and SFTP
  responsiveness.
- Server Stats: collect live CPU usage, memory usage, the top CPU and
  memory processes, and per-volume disk usage over SSH from a saved
  session. Results are shown in sortable tables with human-readable sizes
  (KB/MB/GB) and color-coded summary cards for CPU, memory, and disk.
- DNS Record Inspector: query and inspect DNS records (A, AAAA, CNAME, MX,
  TXT, NS, SOA, SRV, PTR, CAA) for any domain, including query-all and
  reverse-DNS lookups.
- GPG Key Manager: list public and secret GPG keys, and export, import, and
  delete keys using the system `gpg` command.
- Fingerprint Utilities: calculate MD5, SHA-1, and SHA-2 family fingerprints
  for text or files, and compare fingerprints with normalized formatting.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| F2 | Rename the selected remote file or directory |
| Ctrl+U (Cmd+U on macOS) | Upload files |
| Ctrl+D (Cmd+D on macOS) | Download selected files |
| Delete | Delete the selected remote item |
| Ctrl+Delete | Delete the selected remote item |
| Ctrl+S (Cmd+S on macOS) | Add the current SFTP directory to bookmarks |
| Ctrl+L (Cmd+L on macOS) | Clear the terminal buffer |
| Ctrl+Shift+D (Cmd+Shift+D on macOS) | Duplicate the current SSH terminal tab |

## Security

- SSH host fingerprints are recorded and checked on later connections. When a
  host is seen for the first time, Termox shows the fingerprint and asks you to
  confirm before trusting it; the connection is refused until you do.
- Passwords and private-key passphrases are encrypted at rest with the
  platform credential store (DPAPI on Windows, Keychain on macOS, Secret
  Service on Linux) and never written to disk in plain text.
- Private-key authentication is supported with a distinct, encrypted
  passphrase — the account password is never used to decrypt a key.
- Client construction is centralized in
  [Services/SshConnectionFactory.cs](Services/SshConnectionFactory.cs), so
  every connection path (terminal, SFTP, editor, server stats, connection
  test) applies the same host-key policy, timeout, and key handling.
- Shell commands built from saved paths (for example, opening a terminal at a
  bookmarked directory) reject control characters and shell metacharacters,
  so a tampered `sessions.json` cannot inject commands.
- SFTP downloads are confined to the chosen destination folder, with
  path-traversal and symlink checks to prevent writes outside it; recursive
  downloads and deletes also guard against remote symlink cycles.
- Recursive SFTP operations and transfers are serialized per tab with an
  operation gate; closing a tab never tears down the gate or cancellation
  source while background work is still running.
- External tool calls (dig, nslookup, gpg) run with timeouts so a hung system
  utility cannot block the application.
- No analytics or application telemetry is intentionally collected by Termox.

Review the security implementation in
[Services/SshSecurity.cs](Services/SshSecurity.cs),
[Services/CredentialManager.cs](Services/CredentialManager.cs),
[Services/SshConnectionFactory.cs](Services/SshConnectionFactory.cs), and
[Services/LocalPathSafety.cs](Services/LocalPathSafety.cs).

## Installation

Release packages are available on the
[GitHub Releases page](https://github.com/cosqnetwork/termox/releases).
Release packages are self-contained and do not require a separate .NET runtime.

### Windows

Download and run the Windows installer, then launch Termox from the Start menu.

### macOS

Download the DMG, open it, and drag Termox to the Applications folder. The
release workflow signs and notarizes macOS packages when the required Apple
credentials are configured.

### Linux

Install the Debian package on Debian-based systems:

```bash
sudo apt install ./Termox-X.Y.Z-linux-x64.deb
```

Alternatively, extract the portable tar archive:

```bash
tar -xzf Termox-X.Y.Z-linux-x64.tar.gz
```

The Linux packaging script supports `linux-x64`, `linux-arm64`, and `linux-arm`
runtime identifiers. The release workflow currently publishes the x64 package.

## First connection

1. Open the Sessions tab and select New Connection.
2. Enter a connection name, host, username, and SSH port.
3. Enter a password, choose a private key, or use both as appropriate. If the
   private key is encrypted, enter its passphrase in the Private Key Passphrase
   field.
4. Optionally configure connection retry (count and delay), SSH keep-alive,
   and an idle timeout before disconnecting.
5. Select Test Connection to validate the connection details. The first time
   you connect to a host, Termox asks you to verify its host-key fingerprint.
6. Select Save Connection to store the profile.
7. Open the saved session to create a new SSH terminal tab.
8. Use Open SFTP Browser from the session menu to create an SFTP tab.

Keep-alive, idle timeout, and retry settings are saved with the profile and
are used whenever you reconnect from the Sessions or Recently Used lists.

## Development

### Requirements

- .NET 10.0 SDK
- Windows, macOS, or Linux
- A working SSH server for manual connection testing

### Build and test

```bash
git clone https://github.com/cosqnetwork/termox.git
cd termox
dotnet restore tests/Termox.Tests/Termox.Tests.csproj
dotnet test tests/Termox.Tests/Termox.Tests.csproj --configuration Release
dotnet build Termox.csproj --configuration Release
dotnet run
```

The project uses MVVM with Avalonia UI. SSH and SFTP functionality is provided
by SSH.NET. Automated tests are located in `tests/Termox.Tests`.

## CI/CD and releases

GitHub Actions runs CI for pull requests and pushes to `main` or `master`. CI
restores dependencies, runs tests, builds the application, and uploads coverage
when available.

The Release workflow is manually started from the default branch. It accepts a
`patch`, `minor`, or `major` bump, finds the newest `vX.Y.Z` tag, calculates the
next version, builds Windows, Linux, and macOS packages, verifies artifacts,
generates `SHA256SUMS.txt`, and publishes a GitHub Release.

See the [CI/CD integration guide](docs/CI-CD-INTEGRATION.md) for repository
permissions, Apple signing secrets, release procedures, and troubleshooting.

## Project structure

```text
Views/          Avalonia windows and controls
ViewModels/     MVVM application and tab logic
Models/         Connection, bookmark, and remote file models
Services/       SSH security, credentials, path safety, and tool services
                (DNS inspection, GPG keys, fingerprints, server stats)
Assets/         Application icons and font resources
packaging/      Windows, Linux, and macOS packaging scripts
tests/          Automated tests
```

## Contributing

1. Fork the repository.
2. Create a feature branch.
3. Make the change and add or update tests where appropriate.
4. Run the build and test commands locally.
5. Open a pull request with a clear description and testing notes.

For bug reports, include the operating system, Termox version, connection type,
steps to reproduce, expected behavior, and actual behavior. Do not include
passwords, private keys, or other sensitive connection information.

## License

Termox is released under the MIT License. See [LICENSE](LICENSE) for details.

Copyright 2026 COSQ NETWORK PRIVATE LIMITED.

## Contact

COSQ NETWORK PRIVATE LIMITED

- Website: https://cosqnetwork.com/
- Address: TC 15/4247-4, 2nd Floor, Horizon Tower, Pattom,
  Thiruvananthapuram, Kerala 695004
- Phone: +91 8078078789

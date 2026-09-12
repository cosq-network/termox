# Termox

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue.svg)

Termox is a cross-platform desktop SSH terminal, remote file manager, server administration workbench, and AI-assisted system operations client. Built with Avalonia UI and .NET 10, it combines high-performance terminal emulation, SFTP file management, in-place remote text editing, systemd and certbot automation, network diagnostics, and an extensible LLM chat assistant ("Helm") in a unified desktop interface.

Termox is developed and maintained by **COSQ NETWORK PRIVATE LIMITED**.

---

## Table of Contents

- [Key Features](#key-features)
  - [SSH Terminal](#ssh-terminal)
  - [SFTP Browser & In-Place Text Editor](#sftp-browser--in-place-text-editor)
  - [AI Chat Assistant (Helm)](#ai-chat-assistant-helm)
  - [Server Management Tools](#server-management-tools)
  - [Network & Diagnostic Utilities](#network--diagnostic-utilities)
  - [Workspace, Tabs & Split View](#workspace-tabs--split-view)
  - [Sessions & Bookmarks](#sessions--bookmarks)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Security Architecture](#security-architecture)
- [Installation](#installation)
- [Development & Testing](#development--testing)
- [CI/CD & Releases](#cicd--releases)
- [Project Structure](#project-structure)
- [Contributing](#contributing)
- [License & Contact](#license--contact)

---

## Key Features

### SSH Terminal

- **Concurrent Tabbed Sessions**: Open multiple independent SSH connections in dedicated tabs.
- **Flexible Authentication**: Authenticate via password, OpenSSH private key (RSA, ED25519), or both. Encrypted private keys use a dedicated passphrase (never confused with account passwords).
- **Session Restoration**: Automatically restore active terminal and SFTP sessions between application runs (`sessions.json`).
- **Keep-Alive Heartbeats**: Configurable keep-alive interval (default 60 seconds) prevents NAT, proxy, and firewall drops.
- **Idle Timeout Warnings**: Optional idle disconnect timeout warns approximately 60 seconds before disconnect; set to `0` to keep connections open indefinitely.
- **Connection Retry Policy**: Configurable retry count and delay on connection drops, configurable per profile.
- **ANSI Terminal Emulation**: Full color terminal buffer rendered with dark theme ergonomics.
- **Tab Duplication & Quick Launch**: Duplicate the active terminal with `Ctrl+Shift+D` (`Cmd+Shift+D` on macOS).
- **Terminal Context Actions**: Right-click to Copy, Paste, Duplicate Tab, open an SFTP browser directly at the current remote directory (`pwd`), or bookmark the current remote path with one click.
- **Buffer Clearing**: Quick buffer clearing with `Ctrl+L` (`Cmd+L` on macOS).

### SFTP Browser & In-Place Text Editor

- **Remote Filesystem Navigation**: Explore remote directory trees, navigate to parent folders, and refresh with live status.
- **Directory Search Filter**: Filter visible remote files and directories with instant, case-insensitive text matching.
- **File Attribute Inspector**: Inspect file names, sizes, file types, last modified dates, full paths, and Unix octal permissions.
- **Upload & Download**: Upload multiple files via local file pickers or download selections directly to your machine.
- **Large Transfer Guard**: Automatic threshold warning (configurable, 100 MB default) prompts confirmation and lists the largest files before large transfers start.
- **In-Place Remote Text Editing**: Open and edit remote configuration files and scripts directly with AvaloniaEdit. Includes dirty-state tracking (`Save*`), save and close, and discard confirmations.
- **Quick Text Preview**: Read-only preview modal for remote text files with line and character counts and quick clipboard copying.
- **File & Folder Renaming**: Rename remote items in-line or with `F2`.
- **Single & Bulk Deletions**: Delete individual items or bulk-delete multiple selections with explicit confirmation modals.
- **Unix Permissions Editor (chmod)**: Change file and directory permissions via an interactive modal accepting standard 3- or 4-digit octal notation (e.g. `755`, `644`).
- **Path Confinement & Symlink Safety**: Download paths are strictly sandboxed to destination directories, and directory recursions guard against symlink cycles.

### AI Chat Assistant (Helm)

Termox includes an integrated LLM system administrator ("Helm") accessible directly from the sidebar. You provide your own OpenAI-compatible endpoint, and Helm can inspect, diagnose, and manage your servers using Termox's native backend tools.

- **Custom Inference Endpoints**: Connect to any OpenAI-compatible provider (OpenRouter, OpenAI, Groq, Ollama, LM Studio, etc.) by configuring Base URL, API Key, Model, Temperature, and Context Window.
- **Live SSE Streaming**: Stream responses token by token with live transcript rendering.
- **15+ Native Tool Calling Integrations**:
  - `list_connections`: List all saved connection profiles.
  - `ssh_run_command`: Execute non-interactive shell commands (with optional elevated `sudo`).
  - `sftp_list_directory` & `sftp_read_text_file`: Browse and read remote configuration or log files.
  - `sftp_upload_file` & `sftp_download_file`: Move files between local workstation and remote hosts.
  - `sftp_rename`, `sftp_chmod`, `sftp_delete`: Remote file management.
  - `sftp_transfer_between_servers`: Stream or direct-transfer files across two remote servers.
  - `systemd_service_*`: Query status, start, stop, restart, enable, or disable remote systemd services.
  - `certbot_*`: Check installation, install certbot, list certificates, obtain Let's Encrypt certificates (dry-run by default), renew, or revoke.
  - `dns_lookup`: Query DNS records (A, AAAA, MX, TXT, NS, SOA, SRV, PTR, CAA).
  - `port_scan` & `ping`: Test TCP ports and ICMP reachability.
  - `calculate_fingerprint`: Hash text or compare fingerprints.
  - `gpg_list_public_keys` & `gpg_delete_key`: Manage local GPG keyrings.
- **3-Tier Risk Security Model**:
  - **Auto**: Read-only queries (DNS, ping, port checks, file reading, directory listing) run immediately.
  - **RequiresApproval**: State-mutating commands (SSH execution, uploads, chmod, service control, certbot issuance) require explicit user confirmation via an in-chat approval banner.
  - **Destructive**: Irreversible operations (file deletion, certificate revocation, GPG key deletion) require explicit user approval.
- **Server Scoping**: Lock the chat session to a single server from the composer picker so the model cannot target unselected hosts.
- **Fail-Closed Host-Key Pinning**: The assistant structurally refuses to connect to servers that have not been interactively verified by the user.
- **Encrypted Local Persistence**: Chat sessions and messages are persisted to local SQLite (`termox.db`), with all message contents and tool call payloads encrypted at rest via the platform credential store (`ChatContentCipher`).
- **First-Party Markdown Engine**: Assistant responses are rendered with a custom visual markdown builder supporting headers, code blocks, tables, bold, and italics.
- **Sticky Question Header**: When scrolling through long assistant explanations, the original user prompt stays pinned at the top of the transcript for instant context.

### Server Management Tools

Accessible from the sidebar and openable into dedicated tabs:

- **Server Stats**: Collects live CPU load, memory utilization, top CPU/memory process lists, and per-volume disk usage over SSH. Formats output into sortable tables with human-readable sizes (KB/MB/GB) and color-coded resource cards.
- **Systemd Service Manager**: Inspect and control remote Linux systemd service units over SSH. Includes action buttons for Start, Stop, Restart, Enable, and Disable, along with quick-select chips for common daemons (`nginx`, `apache2`, `docker`, `postgresql`, `mysql`, `ssh`, `redis`, etc.).
- **Certbot SSL/TLS Manager**: Automate Let's Encrypt certificate management on remote servers over SSH:
  - Check certbot installation status or install certbot automatically via the host's package manager (`apt`, `dnf`, `yum`, `apk`, `pacman`).
  - List existing managed certificates and expiration details.
  - Request certificates using Standalone, Webroot, Nginx, or Apache challenge plugins. Defaults to safe dry runs to protect Let's Encrypt weekly rate limits.
  - Renew all due certificates or revoke/delete existing certificates.
- **Server-to-Server File Transfer**: Transfer files directly between two remote SSH servers without downloading them to your local workstation:
  - **Relay Mode** (Default): Streams data through Termox in memory; works between any two reachable servers without special remote configuration.
  - **Direct Mode**: Runs `rsync` or `scp` directly on the source server targeting the destination (for high-speed transfers when remote SSH trust is already configured).

### Network & Diagnostic Utilities

- **Reorderable Tools List**: Customize the order of the sidebar Tools list via drag-and-drop. Order preferences are automatically saved to disk (`tools_order.json`).
- **DNS Record Inspector**: Query and analyze DNS records (A, AAAA, CNAME, MX, TXT, NS, SOA, SRV, PTR, CAA) with query-all and reverse DNS support.
- **GPG Key Manager**: Inspect public and secret keyrings, import/export ASCII-armored keys, and delete keys using the system `gpg` command.
- **Fingerprint Utilities**: Calculate and compare MD5, SHA-1, SHA-256, SHA-384, and SHA-512 hashes from raw text or local files, featuring format normalization and SSH-style colon-separated display.
- **Port Scanner**: Fast multi-port TCP connectivity scanner to detect open services on remote hosts.
- **Ping Test**: Send ICMP echo requests and monitor latency and packet reachability.
- **SSH Key Generator**: Generate RSA (2048/4096-bit) or ED25519 key pairs using system `ssh-keygen`.
- **Connection Tester**: Batch-test all saved SSH connection profiles simultaneously and view response times and reachability statuses.
- **SSH Endpoint Test**: Probe specific SSH and SFTP endpoints for authentication responsiveness and banner negotiation.

### Workspace, Tabs & Split View

- **Side-by-Side Split View**: Pin any tab into a secondary split pane to view two tasks simultaneously (e.g., monitor Server Stats while working in the Terminal, or view SFTP files while using the Chat Assistant).
- **Browser-Style History Navigation**: Use Back and Forward tab navigation buttons to retrace your tab switching path.
- **Bulk Tab Operations**: Right-click tab context actions to Close Tab, Close Other Tabs, Close Tabs to the Left, Close Tabs to the Right, or Close All Tabs with confirmation safeguards.
- **Custom Frameless Title Bar**: Clean, native-looking window header with custom window controls (minimize, maximize/restore, close) and smooth drag behavior.

### Sessions & Bookmarks

- **Profile Manager**: Save host, port, username, credentials, retry policies, and timeouts into organized profiles.
- **Recently Used Sessions**: Instant one-click access to your 10 most recently opened sessions for Terminal or SFTP launching.
- **Bookmarks Library**: Store remote directory paths tied to specific connection profiles.
- **Favorite Pinning**: Star important bookmarks to keep them pinned at the top of the Bookmarks tab.
- **Launch into Terminal or SFTP**: Open any saved session or bookmark directly into a new SSH shell or SFTP file explorer.

---

## Keyboard Shortcuts

| Shortcut | Context | Action |
| :--- | :--- | :--- |
| `Ctrl+Shift+D` (`Cmd+Shift+D`) | SSH Terminal | Duplicate current terminal tab |
| `Ctrl+L` (`Cmd+L`) | SSH Terminal | Clear terminal buffer |
| `Ctrl+V` (`Cmd+V`) | SSH Terminal | Paste clipboard into terminal |
| `F2` | SFTP Browser | Rename selected remote file or directory |
| `Ctrl+U` (`Cmd+U`) | SFTP Browser | Upload files |
| `Ctrl+D` (`Cmd+D`) | SFTP Browser | Download selected files |
| `Delete` | SFTP Browser | Delete selected remote item |
| `Ctrl+S` (`Cmd+S`) | SFTP Browser | Bookmark current remote directory |
| `Enter` | Chat Assistant | Send message |
| `Shift+Enter` | Chat Assistant | Insert newline in chat composer |
| `Escape` | Global Modals | Dismiss active dialog / preview / modal |

---

## Security Architecture

Security is a primary design tenet of Termox:

- **Host Key Pinning (Trust-on-First-Use)**: Remote host key fingerprints are verified on every connection. The first time a host is contacted, Termox presents the fingerprint for user verification; connections are refused until accepted.
- **Zero-Plaintext Credential Storage**: Passwords, private-key passphrases, and AI API keys are encrypted at rest using platform-native security APIs:
  - **Windows**: Data Protection API (`DPAPI` / `ProtectedData`) scoped to the current user.
  - **macOS**: System Keychain via `/usr/bin/security`.
  - **Linux**: Freedesktop Secret Service API via `secret-tool`.
- **Encrypted Chat Database**: SQLite chat history content and tool call parameters are encrypted per-message with `ChatContentCipher` using platform-secured encryption keys.
- **Centralized Connection Factory**: All SSH/SFTP connections (terminals, SFTP, editor, diagnostics, tools, chat agent) funnel through [`SshConnectionFactory`](Services/SshConnectionFactory.cs) to ensure uniform host-key policy, timeouts, and key handling.
- **Shell Metacharacter Sanitization**: Remote paths and commands are sanitized to prevent shell injection vulnerabilities.
- **SFTP Directory Traversal Guards**: Local download destinations are strictly checked with symlink cycle resolution ([`LocalPathSafety`](Services/LocalPathSafety.cs)) to prevent writes outside destination folders.
- **Zero Telemetry**: Termox does not collect or transmit analytics, crash logs, or application telemetry.

---

## Installation

Download self-contained packages from the [GitHub Releases page](https://github.com/cosqnetwork/termox/releases). Self-contained builds include the .NET runtime.

### Windows

Download and run the Windows installer (`.exe`), or install via the `.msi` or `.msix` package.

### macOS

Download the `.dmg`, open it, and drag Termox to your `Applications` folder.

### Linux

Install the Debian package on Ubuntu/Debian:

```bash
sudo apt install ./Termox-X.Y.Z-linux-x64.deb
```

Install the RPM package on Fedora/RHEL/CentOS:

```bash
sudo rpm -i ./Termox-X.Y.Z-linux-x64.rpm
```

Or extract the portable tarball:

```bash
tar -xzf Termox-X.Y.Z-linux-x64.tar.gz
./Termox
```

---

## Development & Testing

### Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Supported OS: Windows 10/11, macOS 12+, or modern Linux distributions (Ubuntu, Fedora, Arch)
- Optional: An accessible SSH server for manual testing

### Build and Run

```bash
# Clone the repository
git clone https://github.com/cosqnetwork/termox.git
cd termox

# Restore dependencies
dotnet restore tests/Termox.Tests/Termox.Tests.csproj

# Run automated tests
dotnet test tests/Termox.Tests/Termox.Tests.csproj --configuration Release

# Build and run Termox
dotnet build Termox.csproj --configuration Release
dotnet run --project Termox.csproj
```

---

## CI/CD & Releases

Termox uses GitHub Actions with a GitFlow branch model (`main`, `dev`, `release/**`, `hotfix/**`):

- **Continuous Integration**: Builds and runs unit/integration tests across Windows, macOS, and Linux runners on pull requests and branch pushes.
- **Automated Releases**: Pushing to `main` evaluates Conventional Commits to determine Semantic Versioning, builds installers for Windows (EXE, MSI, MSIX), macOS (DMG, ZIP), and Linux (DEB, RPM, TAR.GZ), signs macOS binaries when configured, computes `SHA256SUMS.txt`, and publishes a GitHub Release.

For more details, see the [CI/CD Integration Guide](docs/CI-CD-INTEGRATION.md).

---

## Project Structure

```text
termox/
├── Models/                 # Domain models (Profiles, Bookmarks, RemoteFiles, Chat Messages & Tools)
├── Services/               # Core business logic:
│   ├── SshConnectionFactory.cs   # Centralized SSH/SFTP client instantiation
│   ├── SshSecurity.cs            # Host key verification & security policies
│   ├── CredentialManager.cs      # Native DPAPI / Keychain / Secret Service integration
│   ├── ChatToolRegistry.cs       # AI function-calling dispatch & safety levels
│   ├── ChatHistoryService.cs     # Local SQLite session persistence
│   ├── OpenAiChatClient.cs       # SSE streaming OpenAI-compatible client
│   ├── CertbotService.cs         # Remote SSL/TLS certificate management
│   ├── SystemdService.cs         # Remote Linux systemd unit control
│   ├── ServerStatsService.cs     # Real-time resource metrics collection
│   ├── SftpToolService.cs        # Stateless SFTP operations & server transfer
│   └── LocalPathSafety.cs        # Path traversal & symlink recursion protection
├── ViewModels/             # MVVM ViewModels for application tabs & modal dialogs
├── Views/                  # Avalonia XAML views (MainWindow.axaml)
├── Assets/                 # Fonts, branding icons, and application vector assets
├── packaging/              # Platform packaging configurations (Wix, Debian, macOS plist)
├── tests/                  # Automated test suite (Termox.Tests)
└── docs/                   # Developer documentation & CI/CD guides
```

---

## Contributing

Contributions are welcome! Please follow these steps:

1. Fork the repository and create a feature branch (`git checkout -b feature/my-feature`).
2. Implement your changes and add corresponding unit tests in `tests/Termox.Tests`.
3. Verify that all tests pass: `dotnet test tests/Termox.Tests/Termox.Tests.csproj`.
4. Commit your changes using [Conventional Commits](https://www.conventionalcommits.org/) (`feat: ...`, `fix: ...`).
5. Push to your branch and open a Pull Request.

Please see [CONTRIBUTING.md](CONTRIBUTING.md) for full guidelines.

---

## License & Contact

Termox is open-source software licensed under the [MIT License](LICENSE).

Copyright © 2026 **COSQ NETWORK PRIVATE LIMITED**.

- **Website**: [https://cosqnetwork.com/](https://cosqnetwork.com/)
- **Address**: TC 15/4247-4, 2nd Floor, Horizon Tower, Pattom, Thiruvananthapuram, Kerala 695004
- **Phone**: +91 8078078789

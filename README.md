# Termox - Professional SSH/SFTP Client

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Status](https://img.shields.io/badge/status-production--ready-brightgreen.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue.svg)

**Termox** is an enterprise-grade SSH and SFTP client built with modern UI/UX principles, developed and maintained by **COSQ NETWORK PRIVATE LIMITED**.

## 🎯 Overview

Termox transforms remote SSH management with a comprehensive suite of features for system administrators, DevOps engineers, and developers. Combining terminal emulation, SFTP file management, and advanced network utilities into a single, elegant application.

**From code to cloud** – Termox equips your remote access workflows with the tools you need to thrive.

## ✨ Key Features

### 🔐 SSH Terminal Access
- Full SSH shell access with terminal emulation
- Multi-tab terminal sessions
- Real-time output streaming
- Command history integration
- ANSI color support

### 📁 SFTP File Management
- Browse remote filesystems
- Upload/download files and directories
- Recursive directory operations
- Drag-and-drop support (framework ready)
- File permissions editor
- Real-time search and filtering
- Bulk file operations
- File preview and properties

### 🔒 Security Features
- Password encryption using DPAPI (Windows) / secure storage (other platforms)
- SSH key-based authentication support
- Saved connection profiles with secure credential storage
- Connection validation

### 🎨 Advanced File Operations
- Single-click file deletion with recursive support
- File renaming with modal dialogs
- Permission management (Unix permissions editor)
- Bulk delete operations with success tracking
- File metadata display (size, permissions, modified date)

### 🔍 Navigation & Discovery
- Bookmark system for quick directory access
- File search and filtering in real-time
- Quick-access navigation
- Connection history

### ⌨️ User-Friendly Interface
- Keyboard shortcuts (F2, Ctrl+U, Ctrl+D, Ctrl+Del, Ctrl+S)
- Right-click context menus
- File preview for text files (15+ formats)
- Professional dark theme
- Color-coded status indicators

### 🛠️ Network & SSH Utilities
- **Port Scanner**: Check open/closed ports on remote hosts
- **Ping Test**: ICMP connectivity testing with latency measurement
- **SSH Key Generator**: Create RSA and ED25519 keypairs
- **Connection Batch Tester**: Test all saved connections simultaneously
- **Speed Test**: Framework for SFTP benchmark testing

## 🚀 Getting Started

### System Requirements
- **OS**: Windows, macOS, or Linux
- **Development**: .NET 10.0 SDK
- **Release users**: Self-contained installers; no separate .NET runtime required
- **Memory**: 256 MB minimum
- **Disk Space**: 100 MB for installation

### Installation

#### Windows
1. Download the latest release from [GitHub Releases](https://github.com/cosqnetwork/termox/releases)
2. Run the installer
3. Launch Termox from Start Menu

#### macOS
1. Download the DMG file from [GitHub Releases](https://github.com/cosqnetwork/termox/releases)
2. Drag Termox to Applications folder
3. Launch from Applications or Spotlight

#### Linux
```bash
# Ubuntu/Debian: download the .deb from GitHub Releases, then run:
sudo apt install ./Termox-X.Y.Z-linux-x64.deb

# Or extract the portable .tar.gz archive
```

### Releases and versioning

GitHub Actions publishes self-contained, versioned installers for Windows,
Linux, and macOS. Maintainers run **Actions → Release → Run workflow** and
choose a `patch`, `minor`, or `major` increment. CI creates the next `vX.Y.Z`
tag, builds every platform, generates `SHA256SUMS.txt`, and publishes the
GitHub Release automatically. Pull requests and pushes to the main branch run
build and test validation without creating a release.

See the [CI/CD integration guide](docs/CI-CD-INTEGRATION.md) for repository
permissions, Apple signing secrets, release procedures, and troubleshooting.

### First Connection
1. Click "New Connection" in the Sessions tab
2. Enter SSH server details:
   - **Connection Name**: Descriptive name
   - **Remote Host**: hostname or IP
   - **Username**: SSH username
   - **Port**: SSH port (default: 22)
   - **Authentication**: Password or Private Key
3. Click "Test Connection" to verify
4. Click "Save Connection" to store profile

## 📖 Usage Guide

### SSH Terminal Access
- **New Tab**: Use "New Connection" to open terminal
- **Multiple Sessions**: Open multiple SSH connections in tabs
- **Copy/Paste**: Right-click for context menu

### SFTP File Management
- **Browse**: Click "Open SFTP Browser" on any connection
- **Upload**: Click "Upload File..." or use Ctrl+U
- **Download**: Select files, click "Download..." or use Ctrl+D
- **Rename**: Click "Rename" or press F2
- **Delete**: Select and press Delete or Ctrl+Del
- **Permissions**: Click "Perms..." to edit file permissions

### Network Tools
- Click "Tools" in Sessions tab to access utilities
- **Port Scanner**: Check service availability
- **Ping Test**: Verify network connectivity
- **Connection Tester**: Batch test all saved profiles
- **Key Generator**: Create SSH keypairs

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| F2 | Rename selected file |
| Ctrl+U | Upload files |
| Ctrl+D | Download selected files |
| Ctrl+Del | Delete selected file |
| Ctrl+S | Save current path as bookmark |

## 🔐 Security

- **Credentials**: Stored using Windows DPAPI, macOS Keychain, or the Linux Secret Service
- **SSH Keys**: Supports RSA, ED25519, ECDSA key algorithms
- **Host verification**: SSH host fingerprints are trusted on first use and pinned for subsequent connections
- **No Telemetry**: Completely private, no data collection

### Best Practices
1. Always use key-based authentication when possible
2. Regularly update SSH keys
3. Use strong passphrases for private keys
4. Restrict access to saved connection profiles
5. Keep software updated for security patches

## 📊 Features by Category

### Session Management
✅ Save connection profiles  
✅ Password encryption  
✅ Session persistence  
✅ Auto-reconnect  
✅ Connection history  

### File Operations
✅ Upload/download  
✅ Rename files  
✅ Delete files/folders  
✅ Edit permissions  
✅ Bulk operations  
✅ File preview  

### User Experience
✅ Keyboard shortcuts  
✅ Context menus  
✅ Dark theme  
✅ Bookmarks  
✅ Search/filter  
✅ Status indicators  

### Network Tools
✅ Port scanner  
✅ Ping test  
✅ Connection tester  
✅ Key generator  
✅ Speed test framework  

## 🏢 About COSQ NETWORK

**COSQ NETWORK PRIVATE LIMITED** is a leading technology company specializing in AI, Cloud, and DevOps solutions.

**Motto**: "From code to cloud, we equip your business with the software and IT infrastructure it needs to thrive."

**Services**:
- AI Chatbot Integration
- Machine Learning Operations (MLOps)
- Artificial Intelligence & Data Solutions
- Cloud & DevOps Architecture
- Data Engineering & Analytics
- Emerging Technologies & Automation

**Contact**:
- 📍 TC 15/4247-4, 2nd Floor, Horizon Tower, Pattom, Thiruvananthapuram, Kerala 695004
- 📞 +91 8078078789
- 🌐 https://cosqnetwork.com/

## 📄 License

Termox is released under the **MIT License**. See [LICENSE](LICENSE) file for details.

Copyright © 2026 **COSQ NETWORK PRIVATE LIMITED**. All Rights Reserved.

### MIT License Summary
- ✅ **Use**: Commercial and private use
- ✅ **Modify**: Create derivative works
- ✅ **Distribute**: Distribute under original or modified form
- ⚠️ **Condition**: Must include original license and copyright
- ❌ **Liability**: Software provided as-is without warranty

## 🤝 Contributing

We welcome contributions! To contribute:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Setup
```bash
git clone https://github.com/cosqnetwork/termox.git
cd termox
dotnet restore tests/Termox.Tests/Termox.Tests.csproj
dotnet test tests/Termox.Tests/Termox.Tests.csproj --configuration Release
dotnet build Termox.csproj --configuration Release
dotnet run
```

### Technology Stack
- **Language**: C#
- **Framework**: Avalonia UI (cross-platform)
- **SSH**: SSH.NET (Renci.SshNet)
- **Target**: .NET 10.0

## 🐛 Reporting Issues

Found a bug? Have a feature request? Please open an [issue](https://github.com/cosqnetwork/termox/issues) with:
- Clear description of the issue
- Steps to reproduce (for bugs)
- Expected vs actual behavior
- System information (OS, .NET version)

## 📚 Documentation

- [CI/CD Integration Guide](docs/CI-CD-INTEGRATION.md)
- [Implemented Features](FEATURES_IMPLEMENTED.md)
- [Enterprise Features](FEATURES_ENTERPRISE.md)
- [Advanced Features](FEATURES_ADVANCED.md)
- [Network Tools](FEATURES_NETWORK_TOOLS.md)
- [Packaging Notes](packaging/README.md)

## 🎯 Roadmap

### Planned Features
- [ ] Drag & drop file uploads
- [x] Cross-platform credential manager integration
- [ ] Connection profiles import/export
- [ ] Bulk file operations (copy, move, rename patterns)
- [ ] Syntax highlighting in file preview
- [ ] Permission calculator UI
- [ ] Port forwarding / tunneling
- [ ] Multi-server command execution
- [ ] Activity log / audit trail

## 💡 Tips & Tricks

### Optimize Performance
1. Use key-based authentication (faster than passwords)
2. Bookmark frequently-used directories
3. Batch test connections during off-peak hours

### Keyboard Efficiency
- Master keyboard shortcuts to work faster
- Use Ctrl+S to quickly bookmark important paths
- Press F2 to rename files without mouse

### Network Troubleshooting
1. Use Port Scanner to verify firewall rules
2. Run Ping Test to check latency
3. Use Connection Tester before critical operations

## 📞 Support

**Issues & Bugs**: [GitHub Issues](https://github.com/cosqnetwork/termox/issues)  
**Email**: contact@cosqnetwork.com  
**Website**: https://cosqnetwork.com/

## 📝 Changelog

### Release history

Release versions are generated from Git tags by GitHub Actions. See the
[CI/CD integration guide](docs/CI-CD-INTEGRATION.md) for the versioning policy
and release process.

### Baseline 1.0.0
- Initial product baseline
- SSH terminal access
- SFTP file management
- Network & SSH utilities
- Advanced file operations
- Security features (encryption, bookmarks, session persistence)

## ⭐ Show Your Support

If Termox helps you with your remote access workflows, please:
- ⭐ Star this repository
- 🔗 Share with colleagues
- 📝 Leave feedback
- 🐛 Report issues
- 🤝 Contribute code

## 🙏 Acknowledgments

Termox is built with:
- [Avalonia UI](https://avaloniaui.net/) - Cross-platform UI framework
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SSH library
- [.NET 10.0](https://dotnet.microsoft.com/) - Runtime platform

## 📄 Legal

© 2026 **COSQ NETWORK PRIVATE LIMITED**. All Rights Reserved.

Termox is provided under the MIT License. See LICENSE file for full terms.

---

**Made with ❤️ by COSQ NETWORK PRIVATE LIMITED**

*From code to cloud, we equip your business with the software and IT infrastructure it needs to thrive.*

Visit us: https://cosqnetwork.com/

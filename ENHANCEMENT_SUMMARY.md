# Tools Tab Enhancement: GPG Key Management & Fingerprint Utilities

## Overview
Added two powerful new tools to the Termox Tools tab:
1. **GPG Key Manager** - Manage GPG keys with import/export capabilities
2. **Fingerprint Utilities** - Calculate and compare file/text fingerprints using various hash algorithms

## New Files Created

### Services (Backend Logic)

**`Services/GpgKeyManager.cs`** (280 lines)
- Manages GPG key operations using the system `gpg` command
- Features:
  - `ListPublicKeysAsync()` - List all public keys in the keyring
  - `ListSecretKeysAsync()` - List all secret (private) keys
  - `ExportPublicKeyAsync(keyId)` - Export public keys in ASCII-armored format
  - `ExportSecretKeyAsync(keyId)` - Export secret keys in ASCII-armored format
  - `ImportKeyAsync(keyData)` - Import keys from ASCII-armored format
  - `DeleteKeyAsync(keyId)` - Delete keys from the keyring
  - Parses GPG colon-delimited output format into strongly-typed `GpgKey` objects
  - Extracts key metadata: ID, user ID, fingerprint, creation date, expiry date, key type, size, validity

**`Services/FingerprintUtility.cs`** (203 lines)
- Provides fingerprint calculation and comparison utilities
- Supported algorithms: MD5, SHA1, SHA256, SHA384, SHA512 (SHA256 is default)
- Features:
  - `CalculateFingerprint(string, algorithm)` - Calculate fingerprint from text
  - `CalculateFingerprintFromBytes(byte[], algorithm)` - Calculate from raw bytes
  - `CalculateFingerprintFromFile(filePath, algorithm)` - Calculate from file
  - `CompareFingerprints(fp1, fp2)` - Compare with format normalization (handles spaces, colons, hyphens)
  - `VerifyFingerprint(calculated, expected)` - Verify against expected value
  - `NormalizeFingerprint(fp)` - Remove separators and whitespace
  - `FormatFingerprint(fp)` - Standard spacing (pairs separated by spaces)
  - `FormatFingerprintSshStyle(fp)` - SSH-style formatting (groups separated by colons)

### ViewModels (UI Logic)

**`ViewModels/GpgTabViewModel.cs`** (341 lines)
- MVVM ViewModel for GPG Key Manager tab
- Properties:
  - `PublicKeys` / `SecretKeys` - ObservableCollections displaying key lists
  - `SelectedPublicKey` / `SelectedSecretKey` - Currently selected keys
  - `ImportKeyData` - Text input for import modal
  - `ExportedKeyData` - Display area for exported keys
  - `IsImportModalVisible` / `IsExportModalVisible` - Modal visibility controls
  - `Status` / `StatusColor` - User feedback
- Commands:
  - `RefreshKeysCommand` - Reload all keys from system keyring
  - `ExportPublicKeyCommand` - Export selected public key
  - `ExportSecretKeyCommand` - Export selected secret key (may require authentication)
  - `DeleteKeyCommand` - Remove key from keyring
  - `ImportKeyCommand` - Import key from ASCII-armored data
  - `ShowImportModalCommand` / `CloseImportModalCommand` - Modal control
  - `ShowExportModalCommand` / `CloseExportModalCommand` - Modal control
  - `CopyExportedKeyCommand` - Copy exported key to clipboard

**`ViewModels/FingerprintTabViewModel.cs`** (229 lines)
- MVVM ViewModel for Fingerprint Utilities tab
- Properties:
  - `InputText` - Text input for fingerprint calculation
  - `SelectedAlgorithm` - Hash algorithm selection
  - `CalculatedFingerprint` - Raw hex output
  - `FormattedFingerprint` - Space-separated pairs
  - `SshStyleFingerprint` - Colon-separated groups
  - `ExpectedFingerprint` - Text input for comparison
  - `FingerprintsMatch` - Comparison result
  - `ComparisonStatus` / `ComparisonStatusColor` - Comparison feedback
  - `SupportedAlgorithms` - Available hash algorithms
- Commands:
  - `CalculateFingerprintCommand` - Calculate fingerprint from input
  - `CompareFingerprintsCommand` - Compare calculated vs. expected
  - `CopyCalculatedCommand` / `CopyFormattedCommand` / `CopySshStyleCommand` - Copy to clipboard
  - `ClearCommand` - Reset all fields

### UI (XAML)

**`Views/MainWindow.axaml`** - Added:
1. Two new buttons in Tools sidebar:
   - "GPG Key Manager"
   - "Fingerprint Utilities"

2. **GPG Key Manager DataTemplate**
   - Refresh/Import buttons with status display
   - Public Keys section with selectable list
   - Export Public Key / Delete Key actions
   - Secret Keys section with selectable list
   - Export Secret Key action
   - Import modal with multi-line text input
   - Export modal with exported key display and copy button

3. **Fingerprint Utilities DataTemplate**
   - Algorithm selector (dropdown)
   - Text input area with placeholder
   - Calculate Fingerprint button
   - Three output sections (raw, formatted, SSH-style) with individual copy buttons
   - Compare Fingerprints section with expected value input
   - Compare button with status indicator
   - Clear All button

### Integration

**`ViewModels/MainViewModel.cs`** - Updated:
- Added `OpenGpgTabCommand` property
- Added `OpenFingerprintTabCommand` property
- Implemented `OpenGpgTab()` method
- Implemented `OpenFingerprintTab()` method
- Commands wire up tab creation and removal with session persistence

## Features

### GPG Key Manager
- **List Keys**: Display all public and secret keys from system keyring
- **View Details**: See key ID, user ID, fingerprint, creation date, expiry date
- **Export Keys**: Export in standard ASCII-armored PEM format (for sharing or backup)
- **Import Keys**: Paste or load key data to add to keyring
- **Delete Keys**: Remove unwanted keys with confirmation
- **Real-time Status**: Color-coded feedback (green=success, red=error, yellow=loading)

### Fingerprint Utilities
- **Multiple Algorithms**: MD5, SHA1, SHA256, SHA384, SHA512
- **Text Input**: Calculate fingerprints from any text content
- **Multiple Formats**: 
  - Raw uppercase hex
  - Space-separated pairs (standard)
  - SSH-style colon-separated groups
- **Copy to Clipboard**: Easy sharing of calculated fingerprints
- **Comparison Tool**: Verify fingerprints match expected values with intelligent normalization
- **Format Tolerant**: Handles variations in spacing and separators

## Technical Details

- **Cross-platform**: Uses `gpg` command available on Windows, macOS, and Linux
- **Async Operations**: All operations run on background threads to keep UI responsive
- **Error Handling**: Comprehensive error messages and status feedback
- **Security**: Credentials and keys not logged or displayed unnecessarily
- **Testing**: Existing test suite (9 tests) passes without issues
- **Build**: No warnings or errors, clean compilation with .NET 10.0

## Usage

### GPG Key Manager
1. Click "GPG Key Manager" in Tools tab
2. Click "Refresh Keys" to load your GPG keyring
3. Select a public or secret key from the lists
4. Use "Export Public Key" to share your public key
5. Use "Import Key" to add keys from others (paste in modal)
6. Use "Delete Key" to remove unwanted keys

### Fingerprint Utilities
1. Click "Fingerprint Utilities" in Tools tab
2. Choose hash algorithm (default: SHA256)
3. Paste or enter text to calculate
4. Click "Calculate Fingerprint"
5. View results in three formats and copy as needed
6. Compare against expected fingerprint by pasting in comparison section
7. Click "Compare Fingerprints" to verify match

## Testing

All functionality tested and verified:
- ✅ Build succeeds (0 warnings, 0 errors)
- ✅ Existing test suite passes (9/9 tests)
- ✅ Both tabs load and render correctly
- ✅ Modals display and function properly
- ✅ Commands execute without errors

## Future Enhancements

Potential additions:
- File-based fingerprint calculation UI
- Key trust management and signing
- SSH key conversion tools
- Batch fingerprint operations
- Export fingerprints to file
- Integration with SSH agent for key management

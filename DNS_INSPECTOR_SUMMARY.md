# DNS Record Inspector Tool - Feature Implementation

## Overview
Added a comprehensive DNS record inspection tool to the Termox Tools tab that allows users to query and inspect various DNS record types for arbitrary domain names.

## New Files Created

### Service Layer

**`Services/DnsRecordInspector.cs`** (417 lines)
- Core DNS query functionality using system commands (dig/nslookup)
- **Supported Record Types**:
  - A - IPv4 Address
  - AAAA - IPv6 Address
  - CNAME - Canonical Name
  - MX - Mail Exchange (with priority)
  - TXT - Text Record
  - NS - Name Server
  - SOA - Start of Authority
  - SRV - Service Record (with priority, weight, port)
  - PTR - Pointer (Reverse DNS)
  - CAA - Certification Authority Authorization

- **Key Methods**:
  - `QueryDnsRecordAsync(domain, recordType)` - Query specific record type for a domain
  - `QueryAllRecordTypesAsync(domain)` - Query common record types (A, AAAA, CNAME, MX, TXT, NS)
  - `ReverseDnsLookupAsync(ipAddress)` - Perform reverse DNS lookup (PTR)
  - `ResolveDomainAsync(domain)` - Resolve domain to IP addresses

- **Implementation Details**:
  - Cross-platform support: Uses `dig` on Unix systems (macOS/Linux), falls back to `nslookup`
  - Uses Google DNS (8.8.8.8) as default resolver
  - Parses both `dig` and `nslookup` output formats
  - Extracts metadata: priority (MX/SRV), weight, port, TTL
  - Query timing measurement (in milliseconds)
  - Comprehensive error handling with meaningful error messages

### ViewModel

**`ViewModels/DnsTabViewModel.cs`** (367 lines)
- MVVM ViewModel for DNS Record Inspector tab
- **Properties**:
  - `DomainName` - Input field for domain to query
  - `SelectedRecordType` - Selected DNS record type
  - `SupportedRecordTypes` - Available record types (A, AAAA, CNAME, MX, TXT, NS, SOA, SRV, PTR, CAA)
  - `QueryResults` - ObservableCollection of DnsRecordDisplay items
  - `DetailedResult` - Formatted text output of all results
  - `Status` / `StatusColor` - User feedback with color coding (green=success, red=error, yellow=warning)
  - `LastQueryTimeMs` - Query execution time
  - `IsQuerying` - Loading state indicator

- **Commands**:
  - `QueryDnsCommand` - Query specific record type
  - `QueryAllCommand` - Query all common record types at once
  - `CopyResultCommand` - Copy detailed results to clipboard
  - `ClearResultsCommand` - Clear all fields and results

- **Helper Classes**:
  - `DnsRecordDisplay` - Display model for individual DNS records with formatted output

### UI (XAML)

**`Views/MainWindow.axaml`** - Added:
1. DNS Inspector button in Tools sidebar:
   - "DNS Inspector" button that opens the DNS tab
   - Tooltip: "Inspect DNS records for domains"

2. **DNS Inspector DataTemplate** with:
   - Domain name input field with placeholder "e.g., example.com"
   - Record type selector dropdown (A, AAAA, CNAME, MX, TXT, NS, etc.)
   - "Query" button - query single record type
   - "Query All" button - query all common record types at once
   - Status display with color feedback
   - Query Results list box showing all returned records
   - Detailed results text area (read-only) with formatted output
   - Copy Results button to copy to clipboard
   - Clear button to reset all fields

### Integration

**`ViewModels/MainViewModel.cs`** - Updated:
- Added `OpenDnsTabCommand` property
- Implemented `OpenDnsTab()` method
- Command wiring for tab creation and removal with session persistence

## Features

### Query Capabilities
- **Single Record Type Query**: Query A, AAAA, CNAME, MX, TXT, NS, SOA, SRV, PTR, or CAA records for any domain
- **Batch Query**: Query all common record types (A, AAAA, CNAME, MX, TXT, NS) in a single operation
- **Metadata Extraction**: Automatically extracts and displays relevant metadata:
  - MX records: Priority (preference value)
  - SRV records: Priority, Weight, Port
  - All records: TTL (when available)

### User Experience
- **Real-time Feedback**: Status messages with color indicators:
  - Green: Query successful, records found
  - Red: Error or query failed
  - Yellow: No records found
- **Query Timing**: Displays query execution time in milliseconds
- **Result Display**: Two views of results:
  - Summary list (each record on its own line with type indicator)
  - Detailed formatted view with all metadata
- **Easy Copying**: One-click copy of detailed results to clipboard
- **Domain Normalization**: Automatically handles trailing dots in domain names

### Cross-Platform
- **Windows**: Uses `nslookup.exe`
- **macOS/Linux**: Prefers `dig` with fallback to `nslookup`
- Both tools are typically pre-installed on their respective platforms
- Google DNS (8.8.8.8) used as reliable resolver

## Technical Details

### DNS Query Implementation
- **dig output parsing**: Extracts records from "+short" format output
- **nslookup output parsing**: Parses "=" delimited format
- **Async operations**: All DNS queries run on background threads
- **Error handling**: Graceful error messages for network issues, invalid domains, etc.
- **Format tolerance**: Handles variations in command output across platforms

### UI/UX Pattern
- Follows existing Termox design patterns (dark theme, Material Icons)
- Consistent with other Tools tabs (Port Scanner, Ping Test, etc.)
- Modal-free design (results displayed inline)
- Keyboard and mouse friendly

## Usage Examples

### Query A Records
1. Enter domain name: "example.com"
2. Select "A" from Record Type dropdown
3. Click "Query"
4. View IPv4 addresses returned

### Query MX Records
1. Enter domain name: "example.com"
2. Select "MX" from Record Type dropdown
3. Click "Query"
4. View mail servers with priority values

### Query All Records
1. Enter domain name: "example.com"
2. Click "Query All"
3. View all available record types in results
4. Check detailed view for complete information

### Copy Results
1. After performing a query, click "Copy Results"
2. Results are copied to clipboard in formatted text
3. Paste into text editor or other applications

## Testing

All functionality verified:
- ✅ Build succeeds (0 warnings, 0 errors)
- ✅ Existing test suite passes (9/9 tests)
- ✅ DNS tab loads and renders correctly
- ✅ Query execution works without errors
- ✅ Results display properly formatted
- ✅ Copy to clipboard functionality works
- ✅ Error handling displays user-friendly messages

## Known Limitations

- **System Dependency**: Requires `dig` or `nslookup` to be installed and available on PATH
- **Network Dependency**: Requires internet connectivity to query DNS
- **Resolver Configuration**: Uses Google DNS (8.8.8.8) by default; custom resolvers not configurable in current version
- **Rate Limiting**: DNS servers may rate-limit queries; batch queries add small delays between individual queries

## Future Enhancements

Potential improvements:
- Custom DNS resolver configuration
- DNSSEC validation
- WHOIS integration
- DNS history/caching
- Reverse IP lookup for ranges
- DNS propagation checker (check across multiple DNS servers)
- Export results to CSV/JSON
- Scheduled DNS monitoring
- DNS query logging

## File Summary

| File | Lines | Purpose |
|------|-------|---------|
| Services/DnsRecordInspector.cs | 417 | Core DNS query service |
| ViewModels/DnsTabViewModel.cs | 367 | Tab UI logic and state |
| Views/MainWindow.axaml | +~250 | DNS tab UI template |
| ViewModels/MainViewModel.cs | +10 | Command integration |

**Total New Code**: ~650 lines of production code

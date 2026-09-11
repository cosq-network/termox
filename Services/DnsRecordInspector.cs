using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Termox.Services;

/// <summary>
/// Provides DNS record inspection capabilities using system DNS utilities.
/// </summary>
public class DnsRecordInspector
{
    public enum DnsRecordType
    {
        A,           // IPv4 Address
        AAAA,        // IPv6 Address
        CNAME,       // Canonical Name
        MX,          // Mail Exchange
        TXT,         // Text Record
        NS,          // Name Server
        SOA,         // Start of Authority
        SRV,         // Service
        PTR,         // Pointer
        CAA          // Certification Authority Authorization
    }

    public class DnsRecord
    {
        public string RecordType { get; set; } = "";
        public string Value { get; set; } = "";
        public string Priority { get; set; } = "";  // For MX, SRV
        public string TTL { get; set; } = "";
        public string Weight { get; set; } = "";    // For SRV
        public string Port { get; set; } = "";      // For SRV
    }

    public class DnsQueryResult
    {
        public string Domain { get; set; } = "";
        public string RecordType { get; set; } = "";
        public List<DnsRecord> Records { get; set; } = new();
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public long QueryTimeMs { get; set; }
    }

    private static readonly Dictionary<DnsRecordType, string> RecordTypeNames = new()
    {
        { DnsRecordType.A, "A" },
        { DnsRecordType.AAAA, "AAAA" },
        { DnsRecordType.CNAME, "CNAME" },
        { DnsRecordType.MX, "MX" },
        { DnsRecordType.TXT, "TXT" },
        { DnsRecordType.NS, "NS" },
        { DnsRecordType.SOA, "SOA" },
        { DnsRecordType.SRV, "SRV" },
        { DnsRecordType.PTR, "PTR" },
        { DnsRecordType.CAA, "CAA" }
    };

    /// <summary>
    /// Query DNS records for a domain.
    /// </summary>
    public async Task<DnsQueryResult> QueryDnsRecordAsync(string domain, DnsRecordType recordType)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty", nameof(domain));

        var startTime = DateTime.UtcNow;

        try
        {
            // Normalize domain name (remove trailing dot if present)
            domain = domain.TrimEnd('.');

            var records = await GetDnsRecordsAsync(domain, recordType);

            var elapsed = DateTime.UtcNow - startTime;

            return new DnsQueryResult
            {
                Domain = domain,
                RecordType = RecordTypeNames[recordType],
                Records = records,
                Success = records.Count > 0 || recordType == DnsRecordType.CNAME, // CNAME queries might return no records if it's not a CNAME
                ErrorMessage = records.Count == 0 ? "No records found" : "",
                QueryTimeMs = (long)elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            var elapsed = DateTime.UtcNow - startTime;
            return new DnsQueryResult
            {
                Domain = domain,
                RecordType = RecordTypeNames[recordType],
                Records = new List<DnsRecord>(),
                Success = false,
                ErrorMessage = ex.Message,
                QueryTimeMs = (long)elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Query all common DNS record types for a domain.
    /// </summary>
    public async Task<List<DnsQueryResult>> QueryAllRecordTypesAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty", nameof(domain));

        var tasks = new List<Task<DnsQueryResult>>();
        var recordTypes = Enum.GetValues<DnsRecordType>();

        foreach (var recordType in recordTypes)
        {
            tasks.Add(QueryDnsRecordAsync(domain, recordType));
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Get DNS records using nslookup command (cross-platform).
    /// </summary>
    private async Task<List<DnsRecord>> GetDnsRecordsAsync(string domain, DnsRecordType recordType)
    {
        var recordTypeStr = RecordTypeNames[recordType];

        try
        {
            // Try using nslookup first
            if (OperatingSystem.IsWindows())
            {
                return await QueryUsingNslookupAsync(domain, recordTypeStr);
            }
            else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                // Try dig first on Unix systems, fall back to nslookup
                try
                {
                    return await QueryUsingDigAsync(domain, recordTypeStr);
                }
                catch
                {
                    return await QueryUsingNslookupAsync(domain, recordTypeStr);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"DNS query failed: {ex.Message}", ex);
        }

        return new List<DnsRecord>();
    }

    /// <summary>
    /// Query DNS using dig command (preferred on Unix systems).
    /// </summary>
    private async Task<List<DnsRecord>> QueryUsingDigAsync(string domain, string recordType)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dig",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add(domain);
        processInfo.ArgumentList.Add(recordType);
        processInfo.ArgumentList.Add("+short");

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Could not start dig process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        // Must reuse the same WaitForExitAsync() Task in both the race and the comparison —
        // calling it a second time returns a different Task instance, so comparing against a
        // fresh call is a reference-equality check that can spuriously read "timed out" even
        // when the process exited immediately (this is what was actually breaking DNS
        // queries, not the output parsing).
        var waitForExitTask = process.WaitForExitAsync();
        var exited = await Task.WhenAny(waitForExitTask, Task.Delay(TimeSpan.FromSeconds(15)));
        if (exited != waitForExitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("dig timed out after 15 seconds.");
        }
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorTask.Result.Trim())
                ? $"dig exited with code {process.ExitCode}"
                : errorTask.Result.Trim());

        return ParseDigOutput(outputTask.Result, recordType);
    }

    /// <summary>
    /// Query DNS using nslookup command (cross-platform).
    /// </summary>
    private async Task<List<DnsRecord>> QueryUsingNslookupAsync(string domain, string recordType)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "nslookup.exe" : "nslookup",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("-type=" + recordType);
        processInfo.ArgumentList.Add(domain);

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Could not start nslookup process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();
        var exited = await Task.WhenAny(waitForExitTask, Task.Delay(TimeSpan.FromSeconds(15)));
        if (exited != waitForExitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("nslookup timed out after 15 seconds.");
        }
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorTask.Result.Trim())
                ? $"nslookup exited with code {process.ExitCode}"
                : errorTask.Result.Trim());

        return ParseNslookupOutput(outputTask.Result, recordType);
    }

    /// <summary>
    /// Parse dig command output.
    /// </summary>
    private List<DnsRecord> ParseDigOutput(string output, string recordType)
    {
        var records = new List<DnsRecord>();

        if (string.IsNullOrWhiteSpace(output))
            return records;

        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
                continue;

            var record = ParseDigLine(trimmed, recordType);
            if (record != null)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Parse a single dig output line.
    /// </summary>
    private DnsRecord? ParseDigLine(string line, string recordType)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var record = new DnsRecord { RecordType = recordType };

        switch (recordType)
        {
            case "MX":
                if (parts.Length >= 2)
                {
                    record.Priority = parts[0];
                    record.Value = string.Join(" ", parts.Skip(1));
                }
                break;

            case "TXT":
                record.Value = line;
                break;

            case "SRV":
                if (parts.Length >= 4)
                {
                    record.Priority = parts[0];
                    record.Weight = parts[1];
                    record.Port = parts[2];
                    record.Value = parts[3];
                }
                break;

            default:
                record.Value = string.Join(" ", parts);
                break;
        }

        return record;
    }

    /// <summary>
    /// Parse nslookup command output. Windows nslookup always prints a resolver header
    /// ("Server:"/"Address:" for the *local resolver*, not the record being queried)
    /// followed by a blank line before the real answer — the header's own "Address:" line
    /// uses the identical label as a genuine A-record answer, so the two can only be told
    /// apart by that positional boundary, not by matching text (the previous implementation
    /// matched text and discarded every answer line as a result). Most other record types
    /// (MX/TXT/NS) don't even print a "Name:" line at all, so they need their own handling
    /// rather than a single generic "key = value" scan.
    /// </summary>
    internal List<DnsRecord> ParseNslookupOutput(string output, string recordType)
    {
        var records = new List<DnsRecord>();
        if (string.IsNullOrWhiteSpace(output))
            return records;

        var allLines = output.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < allLines.Length && allLines[i].Trim().Length > 0) i++; // resolver header
        while (i < allLines.Length && allLines[i].Trim().Length == 0) i++; // blank separator
        var answerLines = allLines.Skip(i).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        if (recordType == "TXT")
        {
            // Each entry is "<domain>  text =" on one line, then the quoted value on the next.
            foreach (var line in answerLines)
            {
                var quoteStart = line.IndexOf('"');
                var quoteEnd = line.LastIndexOf('"');
                if (quoteStart < 0 || quoteEnd <= quoteStart) continue;
                records.Add(new DnsRecord { RecordType = recordType, Value = line[(quoteStart + 1)..quoteEnd] });
            }
            return records;
        }

        if (recordType == "MX")
        {
            // "google.com    MX preference = 10, mail exchanger = smtp.google.com"
            foreach (var line in answerLines)
            {
                var prefIdx = line.IndexOf("preference =", StringComparison.OrdinalIgnoreCase);
                var exchIdx = line.IndexOf("mail exchanger =", StringComparison.OrdinalIgnoreCase);
                if (prefIdx < 0 || exchIdx < 0 || exchIdx <= prefIdx) continue;
                var priority = line[(prefIdx + "preference =".Length)..exchIdx].Trim().TrimEnd(',').Trim();
                var exchanger = line[(exchIdx + "mail exchanger =".Length)..].Trim();
                records.Add(new DnsRecord { RecordType = recordType, Priority = priority, Value = exchanger });
            }
            return records;
        }

        foreach (var line in answerLines)
        {
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                continue; // label only — the value is the following "Address(es):" line

            if (line.StartsWith("Address", StringComparison.OrdinalIgnoreCase))
            {
                // Matches both "Address:" (single result) and "Addresses:" (multiple, one
                // per line after the colon on Windows nslookup).
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0) continue;
                var value = line[(colonIndex + 1)..].Trim();
                if (value.Length > 0)
                    records.Add(new DnsRecord { RecordType = recordType, Value = value });
                continue;
            }

            if (line.StartsWith("*"))
                continue; // "** server can't find <domain>: NXDOMAIN" style error lines

            var eqIndex = line.LastIndexOf('=');
            if (eqIndex >= 0)
            {
                var eqValue = line[(eqIndex + 1)..].Trim();
                if (eqValue.Length > 0)
                    records.Add(new DnsRecord { RecordType = recordType, Value = eqValue });
                continue;
            }

            // Bare continuation line — Windows nslookup prints only the first value after
            // "Addresses:", then any further values indented on their own line with no
            // label of their own.
            records.Add(new DnsRecord { RecordType = recordType, Value = line });
        }

        return records;
    }

    /// <summary>
    /// Get all supported DNS record types.
    /// </summary>
    public static string[] GetSupportedRecordTypes()
    {
        return RecordTypeNames.Values.ToArray();
    }

    /// <summary>
    /// Perform reverse DNS lookup (PTR record).
    /// </summary>
    public async Task<string> ReverseDnsLookupAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("IP address cannot be empty", nameof(ipAddress));

        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
            return hostEntry.HostName;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Reverse lookup failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolve domain to IP address.
    /// </summary>
    public async Task<List<string>> ResolveDomainAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty", nameof(domain));

        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(domain);
            return hostEntry.AddressList
                .Select(addr => addr.ToString())
                .ToList();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Domain resolution failed: {ex.Message}", ex);
        }
    }
}

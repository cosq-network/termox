using Termox.Services;
using Xunit;

namespace Termox.Tests;

// Fixtures below are verbatim captures of real `nslookup -type=X <domain>` output on
// Windows — the exact format that broke parsing (the resolver's own header "Address:"
// line was indistinguishable from a genuine A-record answer by text alone, and MX/TXT/NS
// don't print a "Name:" line at all, so answer-section detection never triggered for them).
public class DnsRecordInspectorTests
{
    private readonly DnsRecordInspector _inspector = new();

    [Fact]
    public void ParsesSingleARecord()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            Name:    google.com
            Address:  142.250.77.110
            """;

        var records = _inspector.ParseNslookupOutput(output, "A");

        var record = Assert.Single(records);
        Assert.Equal("142.250.77.110", record.Value);
    }

    [Fact]
    public void ParsesMultipleAAddresses()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            Name:    example.com
            Addresses:  1.2.3.4
                      5.6.7.8
            """;

        var records = _inspector.ParseNslookupOutput(output, "A");

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Value == "1.2.3.4");
        Assert.Contains(records, r => r.Value == "5.6.7.8");
    }

    [Fact]
    public void ParsesMxRecord()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            google.com	MX preference = 10, mail exchanger = smtp.google.com
            """;

        var records = _inspector.ParseNslookupOutput(output, "MX");

        var record = Assert.Single(records);
        Assert.Equal("10", record.Priority);
        Assert.Equal("smtp.google.com", record.Value);
    }

    [Fact]
    public void ParsesTxtRecords()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            google.com	text =

            	"v=spf1 include:_spf.google.com ~all"
            google.com	text =

            	"docusign=1b0a6754-49b1-4db5-8540-d2c12664b289"
            """;

        var records = _inspector.ParseNslookupOutput(output, "TXT");

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Value == "v=spf1 include:_spf.google.com ~all");
        Assert.Contains(records, r => r.Value == "docusign=1b0a6754-49b1-4db5-8540-d2c12664b289");
    }

    [Fact]
    public void ParsesNsRecords()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            google.com	nameserver = ns1.google.com
            google.com	nameserver = ns2.google.com
            """;

        var records = _inspector.ParseNslookupOutput(output, "NS");

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Value == "ns1.google.com");
        Assert.Contains(records, r => r.Value == "ns2.google.com");
    }

    [Fact]
    public void EmptyOutputReturnsNoRecords()
    {
        var records = _inspector.ParseNslookupOutput("", "A");
        Assert.Empty(records);
    }

    [Fact]
    public void HeaderOnlyOutputWithNoAnswerReturnsNoRecords()
    {
        const string output = """
            Non-authoritative answer:
            Server:  UnKnown
            Address:  192.168.18.1

            ** server can't find nosuchdomain.invalid: NXDOMAIN
            """;

        var records = _inspector.ParseNslookupOutput(output, "A");
        Assert.Empty(records);
    }
}

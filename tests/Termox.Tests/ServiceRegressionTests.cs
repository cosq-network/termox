using System;
using System.Linq;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ServiceRegressionTests
{
    [Fact]
    public void GpgParserSeparatesAlgorithmAndKeySize()
    {
        var output =
            "pub:u:2048:1:0123456789ABCDEF:1700000000:0::\n" +
            "fpr:::::::::0123456789ABCDEF0123456789ABCDEF01234567:\n" +
            "uid:u::::1700000000::HASH::Alice Example <alice@example.com>::::::::::0\n";

        var key = new GpgKeyManager().ParseGpgOutput(output).Single();

        Assert.Equal("1", key.KeyType);
        Assert.Equal("2048", key.KeySize);
        Assert.Equal("0123456789ABCDEF", key.KeyId);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", key.Fingerprint);
        Assert.Equal("Alice Example <alice@example.com>", key.UserId);
    }

    [Fact]
    public void GpgParserIgnoresMalformedKeyRecords()
    {
        var output = "pub:u\n" +
                     "pub:u:4096:1:FEDCBA98765432100:1700000000:0::\n";

        var keys = new GpgKeyManager().ParseGpgOutput(output);

        var key = Assert.Single(keys);
        Assert.Equal("1", key.KeyType);
        Assert.Equal("4096", key.KeySize);
    }

    [Fact]
    public void DnsQueryAllIncludesEverySupportedRecordType()
    {
        var supported = DnsRecordInspector.GetSupportedRecordTypes();
        var enumNames = Enum.GetNames<DnsRecordInspector.DnsRecordType>();

        Assert.Equal(enumNames.OrderBy(name => name), supported.OrderBy(name => name));
        Assert.Contains("SOA", supported);
        Assert.Contains("SRV", supported);
        Assert.Contains("PTR", supported);
        Assert.Contains("CAA", supported);
    }
}

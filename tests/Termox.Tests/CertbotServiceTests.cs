using System;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class CertbotServiceTests
{
    [Fact]
    public void BuildObtainCommand_StandaloneSingleDomain_BuildsExpectedCommand()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "admin@example.com",
            Plugin = CertbotService.CertbotPlugin.Standalone,
            DryRun = false
        };

        var command = CertbotService.BuildObtainCommand(request);

        Assert.Equal("certbot certonly --non-interactive --agree-tos -m admin@example.com --standalone -d example.com", command);
    }

    [Fact]
    public void BuildObtainCommand_MultipleDomains_IncludesEachWithItsOwnFlag()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com", "www.example.com" },
            Email = "admin@example.com",
            Plugin = CertbotService.CertbotPlugin.Standalone,
            DryRun = false
        };

        var command = CertbotService.BuildObtainCommand(request);

        Assert.Contains("-d example.com", command);
        Assert.Contains("-d www.example.com", command);
    }

    [Fact]
    public void BuildObtainCommand_WebrootPlugin_IncludesWebrootFlag()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "admin@example.com",
            Plugin = CertbotService.CertbotPlugin.Webroot,
            WebrootPath = "/var/www/html",
            DryRun = false
        };

        var command = CertbotService.BuildObtainCommand(request);

        Assert.Contains("--webroot -w /var/www/html", command);
    }

    [Fact]
    public void BuildObtainCommand_DryRunTrue_AppendsDryRunFlag()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "admin@example.com",
            DryRun = true
        };

        var command = CertbotService.BuildObtainCommand(request);

        Assert.EndsWith("--dry-run", command);
    }

    [Fact]
    public void BuildObtainCommand_DryRunFalse_OmitsDryRunFlag()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "admin@example.com",
            DryRun = false
        };

        var command = CertbotService.BuildObtainCommand(request);

        Assert.DoesNotContain("--dry-run", command);
    }

    [Fact]
    public void BuildObtainCommand_NoDomains_Throws()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = Array.Empty<string>(),
            Email = "admin@example.com"
        };

        Assert.Throws<ArgumentException>(() => CertbotService.BuildObtainCommand(request));
    }

    [Theory]
    [InlineData("example.com; rm -rf /")]
    [InlineData("$(whoami).com")]
    [InlineData("`id`.com")]
    [InlineData("exa mple.com")]
    public void BuildObtainCommand_InvalidDomain_Throws(string domain)
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { domain },
            Email = "admin@example.com"
        };

        Assert.Throws<ArgumentException>(() => CertbotService.BuildObtainCommand(request));
    }

    [Fact]
    public void BuildObtainCommand_InvalidEmail_Throws()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "not-an-email"
        };

        Assert.Throws<ArgumentException>(() => CertbotService.BuildObtainCommand(request));
    }

    [Fact]
    public void BuildObtainCommand_WebrootPluginWithoutPath_Throws()
    {
        var request = new CertbotService.CertbotObtainRequest
        {
            Domains = new[] { "example.com" },
            Email = "admin@example.com",
            Plugin = CertbotService.CertbotPlugin.Webroot,
            WebrootPath = ""
        };

        Assert.Throws<ArgumentException>(() => CertbotService.BuildObtainCommand(request));
    }

    [Theory]
    [InlineData("apt-get")]
    [InlineData("dnf")]
    [InlineData("yum")]
    [InlineData("apk")]
    [InlineData("pacman")]
    public void InstallCommand_ChecksEachSupportedPackageManager(string packageManager)
    {
        Assert.Contains($"command -v {packageManager}", CertbotService.InstallCommand);
    }

    [Fact]
    public void InstallCommand_SkipsReinstallIfAlreadyPresent()
    {
        Assert.StartsWith("if command -v certbot", CertbotService.InstallCommand);
    }

    [Fact]
    public void BuildRevokeCommand_ValidName_BuildsExpectedCommand()
    {
        var command = CertbotService.BuildRevokeCommand("example.com");

        Assert.Equal("certbot revoke --cert-name example.com --non-interactive --delete-after-revoke", command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com; rm -rf /")]
    [InlineData("$(whoami)")]
    public void BuildRevokeCommand_InvalidName_Throws(string certName)
    {
        Assert.Throws<ArgumentException>(() => CertbotService.BuildRevokeCommand(certName));
    }
}

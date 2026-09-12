using System;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ServerTransferCommandTests
{
    [Fact]
    public void BuildDirectTransferCommand_ValidInput_PrefersRsyncFallsBackToScp()
    {
        var command = SftpToolService.BuildDirectTransferCommand(
            "/home/user/file.txt", "10.0.0.5", 22, "deploy", "/srv/uploads/file.txt");

        Assert.Contains("if command -v rsync", command);
        Assert.Contains("rsync -az -e 'ssh -p 22", command);
        Assert.Contains("deploy@10.0.0.5:/srv/uploads/file.txt", command);
        Assert.Contains("scp -P 22", command);
    }

    [Fact]
    public void BuildDirectTransferCommand_CustomPort_UsesItForBothToolsPortFlags()
    {
        var command = SftpToolService.BuildDirectTransferCommand(
            "/a", "example.com", 2222, "user", "/b");

        Assert.Contains("ssh -p 2222", command);
        Assert.Contains("scp -P 2222", command);
    }

    [Theory]
    [InlineData("/a; rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    public void BuildDirectTransferCommand_InvalidSourcePath_Throws(string sourcePath)
    {
        Assert.Throws<ArgumentException>(() =>
            SftpToolService.BuildDirectTransferCommand(sourcePath, "example.com", 22, "user", "/b"));
    }

    [Theory]
    [InlineData("/b; rm -rf /")]
    [InlineData("$(whoami)")]
    public void BuildDirectTransferCommand_InvalidDestPath_Throws(string destPath)
    {
        Assert.Throws<ArgumentException>(() =>
            SftpToolService.BuildDirectTransferCommand("/a", "example.com", 22, "user", destPath));
    }

    [Theory]
    [InlineData("example.com; rm -rf /")]
    [InlineData("$(whoami).com")]
    public void BuildDirectTransferCommand_InvalidHost_Throws(string host)
    {
        Assert.Throws<ArgumentException>(() =>
            SftpToolService.BuildDirectTransferCommand("/a", host, 22, "user", "/b"));
    }

    [Theory]
    [InlineData("user; rm -rf /")]
    [InlineData("$(whoami)")]
    public void BuildDirectTransferCommand_InvalidUser_Throws(string user)
    {
        Assert.Throws<ArgumentException>(() =>
            SftpToolService.BuildDirectTransferCommand("/a", "example.com", 22, user, "/b"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void BuildDirectTransferCommand_InvalidPort_Throws(int port)
    {
        Assert.Throws<ArgumentException>(() =>
            SftpToolService.BuildDirectTransferCommand("/a", "example.com", port, "user", "/b"));
    }
}

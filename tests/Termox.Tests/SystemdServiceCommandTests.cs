using System;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class SystemdServiceCommandTests
{
    [Theory]
    [InlineData("status", "systemctl status nginx --no-pager")]
    [InlineData("start", "systemctl start nginx")]
    [InlineData("stop", "systemctl stop nginx")]
    [InlineData("restart", "systemctl restart nginx")]
    [InlineData("enable", "systemctl enable nginx")]
    [InlineData("disable", "systemctl disable nginx")]
    public void BuildCommand_ValidAction_BuildsExpectedCommand(string action, string expected)
    {
        var command = SystemdService.BuildCommand(action, "nginx");
        Assert.Equal(expected, command);
    }

    [Fact]
    public void BuildCommand_TemplatedUnitName_Allowed()
    {
        var command = SystemdService.BuildCommand("restart", "docker@2.service");
        Assert.Equal("systemctl restart docker@2.service", command);
    }

    [Fact]
    public void BuildCommand_UnsupportedAction_Throws()
    {
        Assert.Throws<ArgumentException>(() => SystemdService.BuildCommand("reload-or-die", "nginx"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nginx; rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    [InlineData("ngi nx")]
    public void BuildCommand_InvalidUnitName_Throws(string unitName)
    {
        Assert.Throws<ArgumentException>(() => SystemdService.BuildCommand("start", unitName));
    }

    [Theory]
    [InlineData("testuser is not in the sudoers file.\nThis incident has been reported.")]
    [InlineData("Sorry, try again.")]
    [InlineData("sudo: a password is required")]
    [InlineData("sudo: apt: command not found")]
    [InlineData("sh: systemctl: not found")]
    public void SudoFailureDetector_KnownFailurePhrases_Detected(string output)
    {
        Assert.True(SudoFailureDetector.IsSudoFailure(output, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void SudoFailureDetector_NormalOutput_NotDetectedAsFailure()
    {
        Assert.False(SudoFailureDetector.IsSudoFailure("● nginx.service - A high performance web server\n   Active: active (running)", out _));
    }
}

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Termox.Models;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatToolRegistryTests
{
    [Fact]
    public async Task DispatchWithUnknownProfileIdReturnsFailureNotException()
    {
        var connections = new ObservableCollection<SshConnectionProfile>
        {
            new() { Id = "known", Name = "Known", Host = "example.com", HostKeyFingerprint = "SHA256:abc" }
        };
        var registry = new ChatToolRegistry(connections);
        var call = new ChatToolCall
        {
            Id = "call_1",
            FunctionName = "ssh_run_command",
            ArgumentsJson = "{\"profileId\":\"missing\",\"command\":\"ls\"}"
        };

        var result = await registry.DispatchAsync(call, requestApproval: null);

        Assert.False(result.Success);
        Assert.Contains("Unknown connection profile", result.Content);
    }

    [Fact]
    public async Task DispatchWithUnpinnedHostKeyIsRejectedBeforeConnecting()
    {
        var connections = new ObservableCollection<SshConnectionProfile>
        {
            new() { Id = "never-connected", Name = "Never Connected", Host = "example.com", HostKeyFingerprint = "" }
        };
        var registry = new ChatToolRegistry(connections);
        var call = new ChatToolCall
        {
            Id = "call_1",
            FunctionName = "ssh_run_command",
            ArgumentsJson = "{\"profileId\":\"never-connected\",\"command\":\"ls\"}"
        };

        var result = await registry.DispatchAsync(call, requestApproval: null);

        Assert.False(result.Success);
        Assert.Contains("never been verified interactively", result.Content);
    }

    [Fact]
    public async Task UnknownToolNameReturnsFailure()
    {
        var registry = new ChatToolRegistry(new ObservableCollection<SshConnectionProfile>());
        var call = new ChatToolCall { Id = "call_1", FunctionName = "not_a_real_tool", ArgumentsJson = "{}" };

        var result = await registry.DispatchAsync(call, requestApproval: null);

        Assert.False(result.Success);
        Assert.Contains("Unknown tool", result.Content);
    }

    [Fact]
    public void AutoTierToolsDoNotRequireApproval()
    {
        var registry = new ChatToolRegistry(new ObservableCollection<SshConnectionProfile>());
        var definitions = registry.BuildToolDefinitions();

        var pingTool = definitions.Single(d => d.Name == "ping");
        var deleteTool = definitions.Single(d => d.Name == "sftp_delete");

        Assert.Equal(ChatToolRiskLevel.Auto, pingTool.RiskLevel);
        Assert.Equal(ChatToolRiskLevel.Destructive, deleteTool.RiskLevel);
    }
}

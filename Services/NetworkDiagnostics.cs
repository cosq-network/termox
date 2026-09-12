using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Termox.Services;

public class PingOutcome
{
    public bool Success { get; set; }
    public long RoundtripMs { get; set; }
    public string Status { get; set; } = "";
}

/// <summary>
/// Small reusable network-check helpers, extracted from ToolsTabViewModel's private
/// port-scan/ping handlers so both the manual Tools tab and the chat agent's tools
/// can share the same behavior. ToolsTabViewModel itself is left untouched.
/// </summary>
public static class NetworkDiagnostics
{
    public static async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs = 2000, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port, cancellationToken).AsTask();
            var delayTask = Task.Delay(timeoutMs, cancellationToken);
            var completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<PingOutcome> PingOnceAsync(string host, int timeoutMs = 5000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new PingOutcome { Success = true, RoundtripMs = reply.RoundtripTime, Status = "Success" }
                : new PingOutcome { Success = false, Status = reply.Status.ToString() };
        }
        catch (Exception ex)
        {
            return new PingOutcome { Success = false, Status = $"Error: {ex.Message}" };
        }
    }
}

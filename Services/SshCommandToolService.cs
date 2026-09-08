using System;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Runs a single non-interactive command on a saved connection profile, for use as a
/// chat-agent tool. Deliberately separate from TerminalTabViewModel's interactive
/// ShellStream (safe to run alongside the user's own open terminal tabs) — follows the
/// same short-lived-connection pattern as ServerStatsService.CollectAsync.
/// </summary>
public class SshCommandToolService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public async Task<string> RunCommandAsync(SshConnectionProfile profile, string command, CancellationToken cancellationToken = default)
    {
        ChatHostKeyGuard.EnsurePinned(profile);

        var client = SshConnectionFactory.CreateSshClient(profile);
        SshSecurity.ConfigureHostKeyPolicy(client, profile.HostKeyFingerprint, firstSeen: null, confirmNewHost: null);

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var sshCommand = client.CreateCommand(command);
            sshCommand.CommandTimeout = CommandTimeout;
            await sshCommand.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            var output = sshCommand.Result ?? "";
            var error = sshCommand.Error ?? "";
            return error.Length == 0 ? output : $"{output}\n[stderr]\n{error}";
        }
        finally
        {
            try { client.Disconnect(); } catch { }
            client.Dispose();
        }
    }
}

/// <summary>
/// Shared fail-closed check for agent-initiated SSH/SFTP connections: SshSecurity's
/// host-key policy trusts a host on first use when no expected fingerprint is set
/// (TOFU) — fine for an interactive connect with a confirmation dialog, but a silent
/// trust hole for an unattended tool call. Agent tools must only ever touch profiles
/// that already have a fingerprint pinned from a prior interactive connection.
/// </summary>
internal static class ChatHostKeyGuard
{
    public static void EnsurePinned(SshConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.HostKeyFingerprint))
            throw new InvalidOperationException(
                $"Connection '{profile.Name}' has never been verified interactively. " +
                "Connect once in a Terminal or SFTP tab first so its host key is pinned.");
    }
}

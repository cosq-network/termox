using System;
using System.IO;
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

    public async Task<string> RunCommandAsync(SshConnectionProfile profile, string command, bool sudo = false, CancellationToken cancellationToken = default)
    {
        ChatHostKeyGuard.EnsurePinned(profile);

        var client = SshConnectionFactory.CreateSshClient(profile);
        SshSecurity.ConfigureHostKeyPolicy(client, profile.HostKeyFingerprint, firstSeen: null, confirmNewHost: null);

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (!sudo)
            {
                using var sshCommand = client.CreateCommand(command);
                sshCommand.CommandTimeout = CommandTimeout;
                await sshCommand.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return FormatOutput(sshCommand.Result, sshCommand.Error);
            }

            return await RunSudoCommandAsync(client, profile, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
            client.Dispose();
        }
    }

    /// <summary>
    /// Runs a command via "sudo -S", feeding the connection's stored login password on
    /// stdin so it doesn't appear on the command line (visible to other users via `ps`).
    /// Only works for password-authenticated profiles with passwordless sudo NOT already
    /// configured, or ones where the login password is also the sudo password — the
    /// common case for the single-user boxes this tool targets. Profiles with no stored
    /// password (key-based auth) fail with a clear error instead of hanging on a prompt.
    /// </summary>
    private static async Task<string> RunSudoCommandAsync(
        Renci.SshNet.SshClient client, SshConnectionProfile profile, string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(profile.Password))
            throw new InvalidOperationException(
                $"'{profile.Name}' has no stored login password to authenticate sudo with. " +
                "Either configure passwordless sudo (NOPASSWD) for this account on the server, " +
                "or use a password-authenticated connection profile.");

        // Wrapped in "sh -c '<command>'" (not "sudo -S -- <command>" directly) so a
        // compound command using &&, |, or ; runs as one unit under sudo — a bare "sudo --"
        // only elevates the first segment, leaving the rest of a "cmd1 && cmd2" pipeline
        // running unprivileged.
        var escapedCommand = command.Replace("'", "'\\''");
        var sshCommand = client.CreateCommand($"sudo -S -p '' sh -c '{escapedCommand}'");
        try
        {
            sshCommand.CommandTimeout = CommandTimeout;

            // CreateInputStream() requires the channel to already be open — it throws
            // "The input stream can be used only during execution" otherwise — and the
            // channel only opens once BeginExecute() actually starts running (it calls
            // _channel.Open() internally). So BeginExecute() must come first; calling
            // CreateInputStream() before it (the previous order here) failed every time,
            // unconditionally, not as an occasional race.
            var asyncResult = sshCommand.BeginExecute();
            var stdin = sshCommand.CreateInputStream();
            try
            {
                await using var writer = new StreamWriter(stdin, leaveOpen: true);
                await writer.WriteLineAsync(profile.Password).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // sudo can exit before our password ever reaches its stdin — e.g. no sudo
                // binary on the box, "Defaults requiretty" in sudoers, or the account isn't
                // in sudoers at all (common on minimal Docker sshd images). When that happens
                // the channel is already closed and this write throws a confusing SSH.NET
                // stream-state exception ("input stream can be used only during execution")
                // that has nothing to do with the real cause. Swallow it here so EndExecute
                // below still runs and the actual stderr from sudo reaches the user.
            }

            string? result;
            string error;
            try
            {
                await Task.Run(() => sshCommand.EndExecute(asyncResult), cancellationToken).ConfigureAwait(false);
                result = sshCommand.Result;
                error = sshCommand.Error ?? "";
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // SSH.NET's APM pattern re-throws from EndExecute whatever exception the
                // channel captured internally — including the same stream-state exception
                // above, since the channel closing early (sudo exiting before reading stdin)
                // affects both the write and the completion wait. Whatever stdout/stderr the
                // remote side sent before closing is still captured on the command object;
                // surface that instead of this internal exception, with a clear fallback if
                // the server genuinely sent nothing back.
                result = sshCommand.Result;
                error = sshCommand.Error ?? "";
                if (string.IsNullOrWhiteSpace(result) && string.IsNullOrWhiteSpace(error))
                {
                    error = "sudo exited before completing, with no output. The account may not have " +
                        "sudo access, sudo may not be installed on this host, or the server's sudo " +
                        "configuration may require a real TTY ('Defaults requiretty' in /etc/sudoers).";
                }
            }

            // sudo -S echoes nothing to stdout on success, but on a wrong/missing password it
            // writes "Sorry, try again." / "incorrect password attempts" to stderr — surface
            // that plainly instead of a confusing empty result.
            return FormatOutput(result, error);
        }
        finally
        {
            // Disposing SshCommand also disposes its input stream — which can throw the
            // same "only during execution" exception as above if the channel already
            // closed early. Catch it here too rather than let cleanup mask a result we've
            // already successfully captured and returned above.
            try { sshCommand.Dispose(); } catch { }
        }
    }

    private static string FormatOutput(string? output, string? error)
    {
        var stdout = output ?? "";
        var stderr = error ?? "";
        return stderr.Length == 0 ? stdout : $"{stdout}\n[stderr]\n{stderr}";
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

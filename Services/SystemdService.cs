using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Controls systemd services (start/stop/restart/enable/disable/status) on a remote host
/// over an existing saved SSH connection, via SshCommandToolService — same thin
/// command-builder-plus-runner shape as CertbotService.
/// </summary>
public class SystemdService
{
    private static readonly Regex UnitNamePattern = new(@"^[a-zA-Z0-9@_.\-]+$", RegexOptions.Compiled);

    private readonly SshCommandToolService _sshCommandToolService = new();

    public Task<string> StatusAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("status", unitName), sudo: false, cancellationToken);

    public Task<string> StartAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("start", unitName), sudo: true, cancellationToken);

    public Task<string> StopAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("stop", unitName), sudo: true, cancellationToken);

    public Task<string> RestartAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("restart", unitName), sudo: true, cancellationToken);

    public Task<string> EnableAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("enable", unitName), sudo: true, cancellationToken);

    public Task<string> DisableAsync(SshConnectionProfile profile, string unitName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildCommand("disable", unitName), sudo: true, cancellationToken);

    /// <summary>
    /// Builds the "systemctl &lt;action&gt; &lt;unit&gt;" command line. Pure and side-effect-free
    /// so it's unit testable without an SSH connection. The unit name is validated against a
    /// strict allow-list (systemd unit names use letters, digits, '@' for templated units,
    /// '.', '_', '-') before interpolation, and "status" always appends "--no-pager" so a
    /// long-running pager doesn't hang the non-interactive SSH command.
    /// </summary>
    internal static string BuildCommand(string action, string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName) || !UnitNamePattern.IsMatch(unitName))
            throw new ArgumentException($"'{unitName}' is not a valid systemd unit name.");

        return action switch
        {
            "status" => $"systemctl status {unitName} --no-pager",
            "start" => $"systemctl start {unitName}",
            "stop" => $"systemctl stop {unitName}",
            "restart" => $"systemctl restart {unitName}",
            "enable" => $"systemctl enable {unitName}",
            "disable" => $"systemctl disable {unitName}",
            _ => throw new ArgumentException($"Unsupported systemctl action '{action}'.")
        };
    }
}

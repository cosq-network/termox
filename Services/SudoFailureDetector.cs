using System;

namespace Termox.Services;

/// <summary>
/// Detects known "the remote command didn't actually run" phrasing in output that made it
/// back over SSH without throwing — either sudo refusing it (wrong/no sudo access, not in
/// sudoers, no TTY), or the command itself not existing on the remote host. Neither of
/// these throws — SshCommandToolService.RunCommandAsync just returns whatever stdout/stderr
/// the remote side produced — so callers that assume "no exception means success" show a
/// false green status. This shared check exists because that exact bug was independently
/// hit three times (Certbot's Install button, then the systemd service tab's missing-binary
/// case, on a busybox `sh` that phrases "command not found" differently than bash) before
/// being pulled into one place.
/// </summary>
public static class SudoFailureDetector
{
    public static bool IsSudoFailure(string output, out string reason)
    {
        if (output.Contains("is not in the sudoers file", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Sorry, try again", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("a password is required", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: sudo refused this command — see output below.";
            return true;
        }

        // Bash phrases a missing binary as "bash: X: command not found"; busybox/dash
        // (common on minimal container images) instead prints "sh: X: not found" — no
        // "command" in it — so both substrings need checking, not just one.
        if (output.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
            output.Contains(": not found", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: the remote shell reports the command isn't installed — see output below.";
            return true;
        }

        reason = "";
        return false;
    }
}

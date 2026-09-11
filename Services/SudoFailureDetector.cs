namespace Termox.Services;

/// <summary>
/// Detects known sudo failure phrasing in command output that ran fine over SSH but was
/// refused by sudo on the remote side (wrong/no sudo access, not in sudoers, no TTY). A
/// command like this doesn't throw — SshCommandToolService.RunCommandAsync just returns
/// whatever stdout/stderr the remote side produced — so callers that assume "no exception
/// means success" show a false green status. This shared check exists because that exact
/// bug was caught twice independently (Certbot's Install button, then re-derived for the
/// systemd service tab) before being pulled out into one place.
/// </summary>
public static class SudoFailureDetector
{
    public static bool IsSudoFailure(string output, out string reason)
    {
        if (output.Contains("is not in the sudoers file", System.StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: sudo refused this command — see output below.";
            return true;
        }
        if (output.Contains("incorrect password", System.StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Sorry, try again", System.StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: sudo refused this command — see output below.";
            return true;
        }
        if (output.Contains("a password is required", System.StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: sudo refused this command — see output below.";
            return true;
        }
        if (output.Contains("command not found", System.StringComparison.OrdinalIgnoreCase))
        {
            reason = "Failed: sudo refused this command — see output below.";
            return true;
        }

        reason = "";
        return false;
    }
}

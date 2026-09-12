using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Runs certbot (Let's Encrypt ACME client) commands on a remote host over an existing
/// saved SSH connection, via <see cref="SshCommandToolService"/>. Certbot is a server-side
/// tool — this deliberately holds no local ACME/key logic, it only builds and runs the
/// remote command line and returns whatever certbot printed.
/// </summary>
public class CertbotService
{
    public enum CertbotPlugin
    {
        Standalone,
        Webroot,
        Nginx,
        Apache
    }

    public class CertbotObtainRequest
    {
        public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();
        public string Email { get; init; } = "";
        public CertbotPlugin Plugin { get; init; } = CertbotPlugin.Standalone;
        public string WebrootPath { get; init; } = "";
        public bool DryRun { get; init; } = true;
    }

    private static readonly Regex DomainPattern = new(@"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^\s@'""]+@[^\s@'""]+\.[^\s@'""]+$", RegexOptions.Compiled);
    private static readonly Regex SafePathPattern = new(@"^[a-zA-Z0-9/_.-]+$", RegexOptions.Compiled);
    private static readonly Regex CertNamePattern = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    private readonly SshCommandToolService _sshCommandToolService = new();

    public Task<string> CheckStatusAsync(SshConnectionProfile profile, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile,
            "command -v certbot >/dev/null 2>&1 && certbot --version || echo 'certbot not found on this host'",
            sudo: false, cancellationToken);

    public Task<string> InstallAsync(SshConnectionProfile profile, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, InstallCommand, sudo: true, cancellationToken);

    /// <summary>
    /// Detects the remote host's package manager and installs certbot with it. Tries each
    /// in turn (apt/dnf/yum/apk/pacman cover the vast majority of what runs sshd) rather
    /// than assuming one, since Tools tabs like this one are used against arbitrary saved
    /// hosts, not a single known distro.
    /// </summary>
    internal const string InstallCommand =
        "if command -v certbot >/dev/null 2>&1; then echo 'certbot is already installed.'; " +
        "elif command -v apt-get >/dev/null 2>&1; then apt-get update -y && apt-get install -y certbot; " +
        "elif command -v dnf >/dev/null 2>&1; then dnf install -y certbot; " +
        "elif command -v yum >/dev/null 2>&1; then yum install -y certbot; " +
        "elif command -v apk >/dev/null 2>&1; then apk add --no-cache certbot; " +
        "elif command -v pacman >/dev/null 2>&1; then pacman -Sy --noconfirm certbot; " +
        "else echo 'No supported package manager found (expected apt-get, dnf, yum, apk, or pacman). Install certbot manually.'; exit 1; " +
        "fi";

    public Task<string> ListCertificatesAsync(SshConnectionProfile profile, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, "certbot certificates", sudo: true, cancellationToken);

    public Task<string> ObtainAsync(SshConnectionProfile profile, CertbotObtainRequest request, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildObtainCommand(request), sudo: true, cancellationToken);

    public Task<string> RenewAllAsync(SshConnectionProfile profile, bool dryRun, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, dryRun ? "certbot renew --dry-run" : "certbot renew", sudo: true, cancellationToken);

    public Task<string> RevokeAsync(SshConnectionProfile profile, string certName, CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(profile, BuildRevokeCommand(certName), sudo: true, cancellationToken);

    /// <summary>
    /// Builds the "certbot certonly ..." command line for an obtain/renew request. Pure and
    /// side-effect-free so it can be unit tested without an SSH connection. Domains, email,
    /// and the webroot path are validated against strict allow-lists before interpolation —
    /// SshCommandToolService's own quoting only protects the outer "sh -c '...'" wrapper for
    /// sudo calls, not injection via a crafted value placed inside that command string.
    /// </summary>
    internal static string BuildObtainCommand(CertbotObtainRequest request)
    {
        if (request.Domains == null || request.Domains.Count == 0)
            throw new ArgumentException("At least one domain is required.");

        foreach (var domain in request.Domains)
        {
            if (!DomainPattern.IsMatch(domain))
                throw new ArgumentException($"'{domain}' is not a valid domain name.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !EmailPattern.IsMatch(request.Email))
            throw new ArgumentException("A valid email address is required.");

        if (request.Plugin == CertbotPlugin.Webroot)
        {
            if (string.IsNullOrWhiteSpace(request.WebrootPath))
                throw new ArgumentException("A webroot path is required for the webroot plugin.");
            if (!SafePathPattern.IsMatch(request.WebrootPath))
                throw new ArgumentException($"'{request.WebrootPath}' is not a valid path.");
        }

        var sb = new StringBuilder("certbot certonly --non-interactive --agree-tos -m ")
            .Append(request.Email)
            .Append(' ')
            .Append(PluginFlag(request.Plugin));

        if (request.Plugin == CertbotPlugin.Webroot)
            sb.Append(" -w ").Append(request.WebrootPath);

        foreach (var domain in request.Domains)
            sb.Append(" -d ").Append(domain);

        if (request.DryRun)
            sb.Append(" --dry-run");

        return sb.ToString();
    }

    internal static string BuildRevokeCommand(string certName)
    {
        if (string.IsNullOrWhiteSpace(certName) || !CertNamePattern.IsMatch(certName))
            throw new ArgumentException($"'{certName}' is not a valid certificate name.");

        return $"certbot revoke --cert-name {certName} --non-interactive --delete-after-revoke";
    }

    private static string PluginFlag(CertbotPlugin plugin) => plugin switch
    {
        CertbotPlugin.Standalone => "--standalone",
        CertbotPlugin.Webroot => "--webroot",
        CertbotPlugin.Nginx => "--nginx",
        CertbotPlugin.Apache => "--apache",
        _ => "--standalone"
    };
}

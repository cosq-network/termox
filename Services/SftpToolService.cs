using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Stateless SFTP operations for use as chat-agent tools. Each method opens its own
/// short-lived SftpClient rather than reusing SftpTabViewModel's UI-bound handlers
/// (those are private, wrapped in fire-and-forget Task.Run, and gated by an
/// operation semaphore tied to live selection state — not safe to call from here).
/// </summary>
public class SftpToolService
{
    private const long TextFileLimitBytes = 5 * 1024 * 1024;

    private static readonly Regex SafeRemotePathPattern = new(@"^[a-zA-Z0-9/_.\- ]+$", RegexOptions.Compiled);
    private static readonly Regex SafeHostPattern = new(@"^[a-zA-Z0-9.\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeUserPattern = new(@"^[a-zA-Z0-9_.\-]+$", RegexOptions.Compiled);

    private readonly SshCommandToolService _sshCommandToolService = new();

    public async Task<string> ListDirectoryAsync(SshConnectionProfile profile, string remotePath, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        var entries = client.ListDirectory(remotePath)
            .Where(f => f.Name != "." && f.Name != "..")
            .OrderByDescending(f => f.IsDirectory)
            .ThenBy(f => f.Name)
            .Select(f => $"{(f.IsDirectory ? "d" : "-")} {f.Length,12} {f.LastWriteTime:yyyy-MM-dd HH:mm} {f.Name}");
        return string.Join('\n', entries);
    }

    public async Task<string> ReadTextFileAsync(SshConnectionProfile profile, string remotePath, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        var attributes = client.GetAttributes(remotePath);
        if (attributes.Size > TextFileLimitBytes)
            throw new InvalidOperationException($"File exceeds the {TextFileLimitBytes / (1024 * 1024)}MB read limit for the chat tool.");

        using var stream = client.OpenRead(remotePath);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> UploadFileAsync(SshConnectionProfile profile, string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException("Local file not found.", localPath);

        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        await using var localStream = File.OpenRead(localPath);
        using var remoteStream = client.OpenWrite(remotePath);
        await localStream.CopyToAsync(remoteStream, cancellationToken).ConfigureAwait(false);
        return $"Uploaded '{localPath}' to '{remotePath}'.";
    }

    public async Task<string> DownloadFileAsync(SshConnectionProfile profile, string remotePath, string localDestinationRoot, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(localDestinationRoot);
        var fileName = remotePath.Contains('/') ? remotePath[(remotePath.LastIndexOf('/') + 1)..] : remotePath;
        var localPath = LocalPathSafety.EnsureWithinRoot(localDestinationRoot, Path.Combine(localDestinationRoot, fileName));

        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        using var remoteStream = client.OpenRead(remotePath);
        await using var localStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await remoteStream.CopyToAsync(localStream, cancellationToken).ConfigureAwait(false);
        return $"Downloaded '{remotePath}' to '{localPath}'.";
    }

    public async Task<string> RenameAsync(SshConnectionProfile profile, string remotePath, string newRemotePath, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        client.RenameFile(remotePath, newRemotePath);
        return $"Renamed '{remotePath}' to '{newRemotePath}'.";
    }

    public async Task<string> DeleteAsync(SshConnectionProfile profile, string remotePath, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        var attributes = client.GetAttributes(remotePath);
        if (attributes.IsDirectory)
            client.DeleteDirectory(remotePath);
        else
            client.DeleteFile(remotePath);
        return $"Deleted '{remotePath}'.";
    }

    public async Task<string> ChmodAsync(SshConnectionProfile profile, string remotePath, short mode, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
        client.ChangePermissions(remotePath, mode);
        return $"Changed permissions on '{remotePath}' to {Convert.ToString(mode, 8)}.";
    }

    /// <summary>
    /// Transfers a file directly between two saved SSH servers by streaming it through this
    /// process — read from source's SFTP stream, write straight to destination's SFTP
    /// stream, never touching local disk. This is the general-purpose transfer mode: it
    /// works regardless of whether the two servers can reach each other, since only Termox
    /// needs a path to each of them.
    /// </summary>
    public async Task<string> TransferRelayAsync(
        SshConnectionProfile sourceProfile, string sourceRemotePath,
        SshConnectionProfile destProfile, string destRemotePath,
        CancellationToken cancellationToken = default)
    {
        using var sourceClient = await ConnectAsync(sourceProfile, cancellationToken).ConfigureAwait(false);
        using var destClient = await ConnectAsync(destProfile, cancellationToken).ConfigureAwait(false);
        using var sourceStream = sourceClient.OpenRead(sourceRemotePath);
        using var destStream = destClient.OpenWrite(destRemotePath);
        await sourceStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
        return $"Transferred '{sourceRemotePath}' from '{sourceProfile.Name}' to '{destRemotePath}' on '{destProfile.Name}'.";
    }

    /// <summary>
    /// Transfers a file directly from source to destination's own network path — bytes never
    /// pass through this process — by running rsync/scp ON the source server. Only works if
    /// the source server can already reach the destination over the network AND already has
    /// SSH trust (key-based) configured to it; Termox never sends a stored credential (source
    /// or destination) into this remote command line, so this only takes destination
    /// host/port/user as plain strings, never a destination SshConnectionProfile.
    /// </summary>
    public Task<string> TransferDirectAsync(
        SshConnectionProfile sourceProfile, string sourceRemotePath,
        string destHost, int destPort, string destUser, string destRemotePath,
        CancellationToken cancellationToken = default) =>
        _sshCommandToolService.RunCommandAsync(
            sourceProfile,
            BuildDirectTransferCommand(sourceRemotePath, destHost, destPort, destUser, destRemotePath),
            sudo: false, cancellationToken);

    /// <summary>
    /// Builds the remote command for TransferDirectAsync — prefers rsync (resumable,
    /// preserves permissions) and falls back to scp if rsync isn't installed on the source
    /// server, same "try the better tool, fall back" shape as CertbotService.InstallCommand.
    /// Pure and side-effect-free so it's unit testable without an SSH connection; every
    /// interpolated value is validated against a strict allow-list first since this is a
    /// remote shell command, not just an argument list.
    /// </summary>
    internal static string BuildDirectTransferCommand(
        string sourceRemotePath, string destHost, int destPort, string destUser, string destRemotePath)
    {
        if (string.IsNullOrWhiteSpace(sourceRemotePath) || !SafeRemotePathPattern.IsMatch(sourceRemotePath))
            throw new ArgumentException($"'{sourceRemotePath}' is not a valid remote path.");
        if (string.IsNullOrWhiteSpace(destRemotePath) || !SafeRemotePathPattern.IsMatch(destRemotePath))
            throw new ArgumentException($"'{destRemotePath}' is not a valid remote path.");
        if (string.IsNullOrWhiteSpace(destHost) || !SafeHostPattern.IsMatch(destHost))
            throw new ArgumentException($"'{destHost}' is not a valid host.");
        if (string.IsNullOrWhiteSpace(destUser) || !SafeUserPattern.IsMatch(destUser))
            throw new ArgumentException($"'{destUser}' is not a valid username.");
        if (destPort is < 1 or > 65535)
            throw new ArgumentException($"'{destPort}' is not a valid port.");

        var destination = $"{destUser}@{destHost}:{destRemotePath}";
        return "if command -v rsync >/dev/null 2>&1; then " +
            $"rsync -az -e 'ssh -p {destPort} -o StrictHostKeyChecking=accept-new' '{sourceRemotePath}' '{destination}'; " +
            "else " +
            $"scp -P {destPort} -o StrictHostKeyChecking=accept-new '{sourceRemotePath}' '{destination}'; " +
            "fi";
    }

    private static async Task<Renci.SshNet.SftpClient> ConnectAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        ChatHostKeyGuard.EnsurePinned(profile);

        var client = SshConnectionFactory.CreateSftpClient(profile);
        SshSecurity.ConfigureHostKeyPolicy(client, profile.HostKeyFingerprint, firstSeen: null, confirmNewHost: null);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
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

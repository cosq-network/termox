using System;
using System.IO;
using Renci.SshNet;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Centralizes SSH/SFTP client construction so authentication behavior
/// (key passphrase handling, host-key policy, timeouts) is defined once.
/// </summary>
public static class SshConnectionFactory
{
    /// <summary>
    /// Creates an SftpClient for the given credentials.
    /// The private-key passphrase is used when one is supplied; otherwise the
    /// account password is only used for password authentication.
    /// </summary>
    public static SftpClient CreateSftpClient(string host, int port, string username,
        string password, string privateKeyPath, string? privateKeyPassphrase = null)
    {
        SshSecurity.EnsurePrivateKeyExists(privateKeyPath);

        SftpClient client;
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && File.Exists(privateKeyPath))
        {
            var keyFile = new PrivateKeyFile(privateKeyPath,
                string.IsNullOrEmpty(privateKeyPassphrase) ? null : privateKeyPassphrase);
            client = new SftpClient(host, port, username ?? "", new[] { keyFile });
        }
        else
        {
            client = new SftpClient(host, port, username ?? "", password ?? "");
        }

        client.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;
        return client;
    }

    /// <summary>
    /// Creates an SshClient for the given credentials, with optional keep-alive.
    /// </summary>
    public static SshClient CreateSshClient(string host, int port, string username,
        string password, string privateKeyPath, string? privateKeyPassphrase = null,
        int keepAliveSeconds = 0)
    {
        SshSecurity.EnsurePrivateKeyExists(privateKeyPath);

        SshClient client;
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && File.Exists(privateKeyPath))
        {
            var keyFile = new PrivateKeyFile(privateKeyPath,
                string.IsNullOrEmpty(privateKeyPassphrase) ? null : privateKeyPassphrase);
            client = new SshClient(host, port, username ?? "", new[] { keyFile });
        }
        else
        {
            client = new SshClient(host, port, username ?? "", password ?? "");
        }

        client.ConnectionInfo.Timeout = SshSecurity.ConnectionTimeout;
        if (keepAliveSeconds > 0)
            client.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds);
        return client;
    }

    /// <summary>
    /// Creates an SftpClient from a saved (plaintext-decrypted) profile.
    /// </summary>
    public static SftpClient CreateSftpClient(SshConnectionProfile profile)
    {
        return CreateSftpClient(profile.Host, profile.Port, profile.Username,
            profile.Password, profile.PrivateKeyPath, profile.PrivateKeyPassphrase);
    }

    /// <summary>
    /// Creates an SshClient from a saved (plaintext-decrypted) profile.
    /// </summary>
    public static SshClient CreateSshClient(SshConnectionProfile profile, int keepAliveSeconds = 0)
    {
        return CreateSshClient(profile.Host, profile.Port, profile.Username,
            profile.Password, profile.PrivateKeyPath, profile.PrivateKeyPassphrase, keepAliveSeconds);
    }
}

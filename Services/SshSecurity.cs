using System;
using System.IO;
using Renci.SshNet;

namespace Termox.Services;

public static class SshSecurity
{
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    public static void EnsurePrivateKeyExists(string? privateKeyPath)
    {
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && !File.Exists(privateKeyPath))
            throw new FileNotFoundException("The configured private key file was not found.", privateKeyPath);
    }

    public static void ConfigureHostKeyPolicy(IBaseClient client, string? expectedFingerprint, Action<string>? firstSeen)
    {
        client.HostKeyReceived += (_, args) =>
        {
            var fingerprint = "SHA256:" + args.FingerPrintSHA256;
            var normalizedExpected = NormalizeFingerprint(expectedFingerprint);

            args.CanTrust = FingerprintsMatch(normalizedExpected, fingerprint);

            if (args.CanTrust && string.IsNullOrWhiteSpace(normalizedExpected))
            {
                try
                {
                    firstSeen?.Invoke(fingerprint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to persist host key fingerprint: {ex.Message}");
                    args.CanTrust = false;
                }
            }
        };
    }

    internal static bool FingerprintsMatch(string? expectedFingerprint, string? actualFingerprint)
    {
        if (string.IsNullOrWhiteSpace(actualFingerprint))
            return false;

        var expected = NormalizeFingerprint(expectedFingerprint);
        if (string.IsNullOrWhiteSpace(expected))
            return true; // Trust on first use — fingerprint will be persisted via firstSeen callback.

        return string.Equals(expected, NormalizeFingerprint(actualFingerprint), StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeFingerprint(string? fingerprint)
    {
        return (fingerprint ?? string.Empty).Trim()
            .Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}

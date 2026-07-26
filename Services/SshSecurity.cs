using System;
using Renci.SshNet;

namespace Termox.Services;

public static class SshSecurity
{
    public static void ConfigureHostKeyPolicy(IBaseClient client, string? expectedFingerprint, Action<string>? firstSeen)
    {
        client.HostKeyReceived += (_, args) =>
        {
            var fingerprint = "SHA256:" + args.FingerPrintSHA256;
            var normalizedExpected = NormalizeFingerprint(expectedFingerprint);

            // Trust on first use, then pin the observed key for subsequent connections.
            args.CanTrust = FingerprintsMatch(normalizedExpected, fingerprint);

            if (args.CanTrust && string.IsNullOrWhiteSpace(normalizedExpected))
                firstSeen?.Invoke(fingerprint);
        };
    }

    internal static bool FingerprintsMatch(string? expectedFingerprint, string? actualFingerprint)
    {
        var expected = NormalizeFingerprint(expectedFingerprint);
        return string.IsNullOrWhiteSpace(expected) ||
            string.Equals(expected, NormalizeFingerprint(actualFingerprint), StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeFingerprint(string? fingerprint)
    {
        return (fingerprint ?? string.Empty).Trim()
            .Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}

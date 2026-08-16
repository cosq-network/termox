using System;
using System.Security.Cryptography;
using System.Text;

namespace Termox.Services;

/// <summary>
/// Provides fingerprint calculation and comparison utilities for various hash algorithms.
/// </summary>
public class FingerprintUtility
{
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256,
        SHA384,
        SHA512
    }

    public class FingerprintResult
    {
        public string Algorithm { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string FingerprintFormatted { get; set; } = "";
    }

    /// <summary>
    /// Calculate fingerprint for a string using the specified algorithm.
    /// </summary>
    public static FingerprintResult CalculateFingerprint(string input, HashAlgorithmType algorithm = HashAlgorithmType.SHA256)
    {
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Input cannot be empty", nameof(input));

        var bytes = Encoding.UTF8.GetBytes(input);
        return CalculateFingerprintFromBytes(bytes, algorithm);
    }

    /// <summary>
    /// Calculate fingerprint for bytes using the specified algorithm.
    /// </summary>
    public static FingerprintResult CalculateFingerprintFromBytes(byte[] data, HashAlgorithmType algorithm = HashAlgorithmType.SHA256)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));

        using (var hash = CreateHashAlgorithm(algorithm))
        {
            var hashBytes = hash.ComputeHash(data);
            var fingerprint = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
            var formatted = FormatFingerprint(fingerprint);

            return new FingerprintResult
            {
                Algorithm = algorithm.ToString(),
                Fingerprint = fingerprint,
                FingerprintFormatted = formatted
            };
        }
    }

    /// <summary>
    /// Calculate fingerprint for a file using the specified algorithm.
    /// </summary>
    public static FingerprintResult CalculateFingerprintFromFile(string filePath, HashAlgorithmType algorithm = HashAlgorithmType.SHA256)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be empty", nameof(filePath));

        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException($"File not found: {filePath}");

        using (var stream = System.IO.File.OpenRead(filePath))
        {
            using (var hash = CreateHashAlgorithm(algorithm))
            {
                var hashBytes = hash.ComputeHash(stream);
                var fingerprint = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
                var formatted = FormatFingerprint(fingerprint);

                return new FingerprintResult
                {
                    Algorithm = algorithm.ToString(),
                    Fingerprint = fingerprint,
                    FingerprintFormatted = formatted
                };
            }
        }
    }

    /// <summary>
    /// Compare two fingerprints, accounting for formatting differences and case sensitivity.
    /// </summary>
    public static bool CompareFingerprints(string fingerprint1, string fingerprint2)
    {
        if (string.IsNullOrEmpty(fingerprint1) || string.IsNullOrEmpty(fingerprint2))
            return false;

        // Normalize: remove spaces, colons, hyphens and convert to uppercase
        var normalized1 = NormalizeFingerprint(fingerprint1);
        var normalized2 = NormalizeFingerprint(fingerprint2);

        return normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verify that a calculated fingerprint matches an expected fingerprint.
    /// </summary>
    public static bool VerifyFingerprint(FingerprintResult calculated, string expectedFingerprint)
    {
        return CompareFingerprints(calculated.Fingerprint, expectedFingerprint);
    }

    /// <summary>
    /// Normalize a fingerprint by removing common separators and whitespace.
    /// </summary>
    public static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint))
            return "";

        return fingerprint
            .Replace(":", "")
            .Replace("-", "")
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();
    }

    /// <summary>
    /// Format a fingerprint with standard spacing (pairs of hex digits separated by spaces).
    /// </summary>
    public static string FormatFingerprint(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint))
            return "";

        var normalized = NormalizeFingerprint(fingerprint);
        if (normalized.Length < 2)
            return normalized;

        var sb = new StringBuilder();
        for (int i = 0; i < normalized.Length; i += 2)
        {
            if (i > 0)
                sb.Append(" ");
            if (i + 1 < normalized.Length)
                sb.Append(normalized.Substring(i, 2));
            else
                sb.Append(normalized[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format a fingerprint with SSH-style spacing (groups of 4-5 hex digits separated by colons).
    /// </summary>
    public static string FormatFingerprintSshStyle(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint))
            return "";

        var normalized = NormalizeFingerprint(fingerprint);
        if (normalized.Length < 2)
            return normalized;

        var sb = new StringBuilder();
        int groupSize = normalized.Length <= 32 ? 4 : 2; // Shorter fingerprints get 4-char groups

        for (int i = 0; i < normalized.Length; i += groupSize)
        {
            if (i > 0)
                sb.Append(":");
            sb.Append(normalized.Substring(i, Math.Min(groupSize, normalized.Length - i)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Get all supported hash algorithms.
    /// </summary>
    public static string[] GetSupportedAlgorithms()
    {
        return Enum.GetNames(typeof(HashAlgorithmType));
    }

    private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmType algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmType.MD5 => MD5.Create(),
            HashAlgorithmType.SHA1 => SHA1.Create(),
            HashAlgorithmType.SHA256 => SHA256.Create(),
            HashAlgorithmType.SHA384 => SHA384.Create(),
            HashAlgorithmType.SHA512 => SHA512.Create(),
            _ => SHA256.Create()
        };
    }
}

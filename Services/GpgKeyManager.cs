using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Termox.Services;

/// <summary>
/// Manages GPG key operations including listing, importing, and exporting keys.
/// </summary>
public class GpgKeyManager
{
    public class GpgKey
    {
        public string KeyId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Now;
        public string KeyType { get; set; } = ""; // rsa, dsa, elg, eddsa, etc.
        public string KeySize { get; set; } = ""; // 2048, 4096, etc.
        public string Validity { get; set; } = ""; // u (ultimate), t (trusted), etc.
    }

    /// <summary>
    /// List all public keys in the GPG keyring.
    /// </summary>
    public async Task<List<GpgKey>> ListPublicKeysAsync()
    {
        return await ExecuteGpgCommandAsync("--list-keys", "--with-fingerprint", "--with-colons");
    }

    /// <summary>
    /// List all secret (private) keys in the GPG keyring.
    /// </summary>
    public async Task<List<GpgKey>> ListSecretKeysAsync()
    {
        return await ExecuteGpgCommandAsync("--list-secret-keys", "--with-fingerprint", "--with-colons");
    }

    /// <summary>
    /// Export a public key to ASCII-armored format.
    /// </summary>
    public async Task<string> ExportPublicKeyAsync(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        return await ExecuteGpgCommandAndGetOutputAsync("--armor", "--export", keyId);
    }

    /// <summary>
    /// Export a secret key to ASCII-armored format (requires passphrase).
    /// </summary>
    public async Task<string> ExportSecretKeyAsync(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        return await ExecuteGpgCommandAndGetOutputAsync("--armor", "--export-secret-keys", keyId);
    }

    /// <summary>
    /// Import a key from ASCII-armored format.
    /// </summary>
    public async Task<(bool Success, string Message)> ImportKeyAsync(string keyData)
    {
        if (string.IsNullOrWhiteSpace(keyData))
            throw new ArgumentException("Key data cannot be empty", nameof(keyData));

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg",
                Arguments = "--import",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Could not start gpg process");

            await process.StandardInput.WriteLineAsync(keyData);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();
            var error = errorTask.Result;

            if (process.ExitCode == 0)
            {
                return (true, "Key imported successfully");
            }

            var message = string.IsNullOrEmpty(error) ? $"GPG exited with code {process.ExitCode}" : error;
            return (false, message.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Import failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a key from the keyring.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteKeyAsync(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            processInfo.ArgumentList.Add("--delete-key");
            processInfo.ArgumentList.Add("--batch");
            processInfo.ArgumentList.Add("--yes");
            processInfo.ArgumentList.Add(keyId);

            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Could not start gpg process");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();
            var error = errorTask.Result;

            if (process.ExitCode == 0)
            {
                return (true, "Key deleted successfully");
            }

            var message = string.IsNullOrEmpty(error) ? $"GPG exited with code {process.ExitCode}" : error;
            return (false, message.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a GPG command and parse the output into GpgKey objects.
    /// </summary>
    private async Task<List<GpgKey>> ExecuteGpgCommandAsync(params string[] args)
    {
        try
        {
            var output = await ExecuteGpgCommandAndGetOutputAsync(args);
            return ParseGpgOutput(output);
        }
        catch
        {
            return new List<GpgKey>();
        }
    }

    /// <summary>
    /// Execute a GPG command and return raw output.
    /// </summary>
    private async Task<string> ExecuteGpgCommandAndGetOutputAsync(params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            processInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Could not start gpg process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = errorTask.Result.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"GPG command failed with exit code {process.ExitCode}"
                : error);
        }

        return outputTask.Result;
    }

    /// <summary>
    /// Parse GPG colon-delimited output format into GpgKey objects.
    /// </summary>
    internal List<GpgKey> ParseGpgOutput(string output)
    {
        var keys = new List<GpgKey>();
        var currentKey = (GpgKey?)null;

        foreach (var line in output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':');
            if (parts.Length < 2)
                continue;

            var recordType = parts[0];

            if (recordType == "pub" || recordType == "sec")
            {
                if (parts.Length <= 4)
                    continue;

                if (currentKey != null)
                    keys.Add(currentKey);

                currentKey = new GpgKey
                {
                    KeyType = parts.Length > 3 ? parts[3] : "",
                    KeySize = parts.Length > 2 ? parts[2] : "",
                    Validity = parts[1]
                };

                // Parse creation and expiry dates
                if (parts.Length > 5 && long.TryParse(parts[5], out var created))
                {
                    currentKey.CreatedDate = UnixTimeStampToDateTime(created);
                }

                if (parts.Length > 6 && long.TryParse(parts[6], out var expiry) && expiry > 0)
                {
                    currentKey.ExpiryDate = UnixTimeStampToDateTime(expiry);
                }

                // Extract key ID (last 16 hex characters)
                if (parts.Length > 4 && parts[4].Length >= 16)
                {
                    currentKey.KeyId = parts[4].Substring(parts[4].Length - 16);
                }
            }
            else if (recordType == "uid" && currentKey != null)
            {
                // Parse user ID
                if (parts.Length > 9)
                {
                    var userId = System.Net.WebUtility.HtmlDecode(parts[9]);
                    if (string.IsNullOrEmpty(currentKey.UserId))
                        currentKey.UserId = userId;
                }
            }
            else if (recordType == "fpr" && currentKey != null)
            {
                // Parse fingerprint
                if (parts.Length > 9)
                    currentKey.Fingerprint = parts[9].Trim();
            }
        }

        if (currentKey != null)
            keys.Add(currentKey);

        return keys;
    }

    private static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
        return dateTime;
    }
}

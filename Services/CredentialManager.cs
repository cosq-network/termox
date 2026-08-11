using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace Termox.Services;

/// <summary>
/// Manages secure storage and retrieval of credentials using Data Protection API (DPAPI)
/// Encrypts sensitive data at rest in connection profiles
/// </summary>
public static class CredentialManager
{
    private const string EncryptionPrefix = "ENCRYPTED:";
    private const string KeychainPrefix = "KEYCHAIN:";

    /// <summary>
    /// Encrypts a credential string using DPAPI (Windows only)
    /// </summary>
    public static string EncryptCredential(string credential, string keyId)
    {
        if (string.IsNullOrEmpty(credential))
            return credential;

        if (credential.StartsWith(EncryptionPrefix, StringComparison.Ordinal) ||
            credential.StartsWith(KeychainPrefix, StringComparison.Ordinal))
            return credential; // Already encrypted

        if (OperatingSystem.IsWindows())
        {
            var data = Encoding.UTF8.GetBytes(credential);
            var entropy = Encoding.UTF8.GetBytes(keyId);
            var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            return EncryptionPrefix + Convert.ToBase64String(encrypted);
        }

        StoreInPlatformKeychain(credential, keyId);
        return KeychainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(keyId));
    }

    public static string EncryptCredential(string credential)
    {
        return EncryptCredential(credential, "default");
    }

    private static void StoreInPlatformKeychain(string credential, string keyId)
    {
        if (OperatingSystem.IsMacOS())
        {
            StoreInMacKeychain(credential, keyId);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            RunSecretCommand("secret-tool", ["store", "--label=Termox connection password", "service", "Termox", "username", keyId], credential);
            return;
        }

        throw new PlatformNotSupportedException("No supported secure credential store is available on this platform.");
    }

    /// <summary>
    /// Decrypts a credential string that was encrypted with EncryptCredential
    /// </summary>
    public static string DecryptCredential(string encryptedCredential, string keyId)
    {
        if (string.IsNullOrEmpty(encryptedCredential))
            return encryptedCredential;

        if (!encryptedCredential.StartsWith(EncryptionPrefix, StringComparison.Ordinal) &&
            !encryptedCredential.StartsWith(KeychainPrefix, StringComparison.Ordinal))
            return string.Empty; // Legacy plaintext is not loaded.

        try
        {
            if (encryptedCredential.StartsWith(EncryptionPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
                return DecryptWithDpapi(encryptedCredential.Substring(EncryptionPrefix.Length), keyId);

            if (encryptedCredential.StartsWith(KeychainPrefix, StringComparison.Ordinal))
                return ReadFromPlatformKeychain(keyId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Credential retrieval failed: {ex.Message}");
            throw;
        }

        // Legacy plaintext values are intentionally not returned to new sessions.
        return string.Empty;
    }

    public static string DecryptCredential(string encryptedCredential)
    {
        return DecryptCredential(encryptedCredential, "default");
    }

    [SupportedOSPlatform("windows")]
    private static string DecryptWithDpapi(string encryptedData, string keyId)
    {
        byte[] dataToDecrypt = Convert.FromBase64String(encryptedData);
        byte[] entropy = Encoding.UTF8.GetBytes(keyId);
        byte[] decryptedData = ProtectedData.Unprotect(dataToDecrypt, entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedData);
    }

    private static string ReadFromPlatformKeychain(string keyId)
    {
        if (OperatingSystem.IsMacOS())
            return ReadFromMacKeychain(keyId);

        if (OperatingSystem.IsLinux())
            return RunSecretCommand("secret-tool", ["lookup", "service", "Termox", "username", keyId]);

        return string.Empty;
    }

    private static string GetServiceName(string keyId) => $"Termox/{keyId}";

    [SupportedOSPlatform("macos")]
    private static void StoreInMacKeychain(string credential, string keyId)
    {
        var service = Encoding.UTF8.GetBytes(GetServiceName(keyId));
        var account = Encoding.UTF8.GetBytes(Environment.UserName);
        var password = Encoding.UTF8.GetBytes(credential);
        var status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service,
            (uint)account.Length, account, (uint)password.Length, password, out var item);

        if (status == ErrSecDuplicateItem)
        {
            IntPtr existingPasswordData = IntPtr.Zero;
            if (item != IntPtr.Zero)
            {
                CFRelease(item);
                item = IntPtr.Zero;
            }
            status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service,
                (uint)account.Length, account, out _, out existingPasswordData, out item);
            try
            {
                if (status == 0)
                    status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password);
            }
            finally
            {
                if (existingPasswordData != IntPtr.Zero)
                    SecKeychainItemFreeContent(IntPtr.Zero, existingPasswordData);
            }
        }

        if (item != IntPtr.Zero) CFRelease(item);
        if (status != 0) throw new InvalidOperationException($"macOS Keychain write failed ({status}).");
    }

    [SupportedOSPlatform("macos")]
    private static string ReadFromMacKeychain(string keyId)
    {
        var service = Encoding.UTF8.GetBytes(GetServiceName(keyId));
        var account = Encoding.UTF8.GetBytes(Environment.UserName);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service,
            (uint)account.Length, account, out var passwordLength, out var passwordData, out var item);
        if (status == ErrSecItemNotFound) return string.Empty;
        if (status != 0) throw new InvalidOperationException($"macOS Keychain read failed ({status}).");

        try
        {
            var password = new byte[passwordLength];
            Marshal.Copy(passwordData, password, 0, password.Length);
            return Encoding.UTF8.GetString(password);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private const int ErrSecDuplicateItem = -25299;
    private const int ErrSecItemNotFound = -25300;

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cfTypeRef);

    private static string RunSecretCommand(string command, string[] arguments, string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = standardInput != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start secure credential store command '{command}'.");
        if (standardInput != null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Secure credential store command '{command}' timed out.");
        }

        var output = outputTask.GetAwaiter().GetResult().Trim();
        var error = errorTask.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"{command} failed." : error);
        return output;
    }

    /// <summary>
    /// Checks if a credential is encrypted
    /// </summary>
    public static bool IsEncrypted(string credential)
    {
        return !string.IsNullOrEmpty(credential) &&
            (credential.StartsWith(EncryptionPrefix, StringComparison.Ordinal) || credential.StartsWith(KeychainPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Encrypts an entire connection profile's sensitive data
    /// </summary>
    public static Models.SshConnectionProfile EncryptProfile(Models.SshConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var encrypted = new Models.SshConnectionProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            Password = EncryptCredential(profile.Password, profile.Id),
            PrivateKeyPath = profile.PrivateKeyPath,
            HostKeyFingerprint = profile.HostKeyFingerprint,
            IsConnected = profile.IsConnected
        };
        return encrypted;
    }

    /// <summary>
    /// Decrypts an entire connection profile's sensitive data
    /// </summary>
    public static Models.SshConnectionProfile DecryptProfile(Models.SshConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var decrypted = new Models.SshConnectionProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            Password = DecryptCredential(profile.Password, profile.Id),
            PrivateKeyPath = profile.PrivateKeyPath,
            HostKeyFingerprint = profile.HostKeyFingerprint,
            IsConnected = profile.IsConnected
        };
        return decrypted;
    }
}

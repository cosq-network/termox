using System;
using System.Security.Cryptography;
using System.Text;

namespace Termox.Services;

/// <summary>
/// Self-contained AES-GCM encryption for arbitrary values under one caller-supplied key —
/// pure and dependency-free (no I/O, no OS keychain access), so it's unit testable by
/// construction with a known key. Callers own resolving/persisting the key itself.
/// </summary>
public class ChatContentCipher
{
    private const string Prefix = "GCM:";
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;

    public ChatContentCipher(byte[] key)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes.", nameof(key));
        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(_key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);

        return Prefix + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts a value written by Encrypt. Anything without the "GCM:" prefix is returned
    /// unchanged — callers handle legacy/plaintext formats themselves, this only ever
    /// touches values it recognizes as its own.
    /// </summary>
    public string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        var combined = Convert.FromBase64String(value[Prefix.Length..]);
        if (combined.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Encrypted value is too short to be valid.");

        var nonce = combined[..NonceSizeBytes];
        var tagStart = combined.Length - TagSizeBytes;
        var ciphertext = combined[NonceSizeBytes..tagStart];
        var tag = combined[tagStart..];
        var plaintextBytes = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(_key, TagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySizeBytes);
}

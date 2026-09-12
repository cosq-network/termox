using System;
using System.Security.Cryptography;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatContentCipherTests
{
    private static byte[] TestKey() => new byte[]
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
    };

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var cipher = new ChatContentCipher(TestKey());

        var encrypted = cipher.Encrypt("hello");

        Assert.Equal("hello", cipher.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_ProducesDistinctCiphertextEachTime()
    {
        var cipher = new ChatContentCipher(TestKey());

        var first = cipher.Encrypt("same plaintext");
        var second = cipher.Encrypt("same plaintext");

        Assert.NotEqual(first, second);
        Assert.Equal("same plaintext", cipher.Decrypt(first));
        Assert.Equal("same plaintext", cipher.Decrypt(second));
    }

    [Fact]
    public void TwoMessagesEncryptedInSequence_BothIndependentlyDecryptCorrectly()
    {
        // This is exactly the scenario the old CredentialManager-per-message scheme got
        // wrong on macOS/Linux: encrypting a second value must not corrupt the first.
        var cipher = new ChatContentCipher(TestKey());

        var firstEncrypted = cipher.Encrypt("hello");
        var secondEncrypted = cipher.Encrypt("hi there");

        Assert.Equal("hello", cipher.Decrypt(firstEncrypted));
        Assert.Equal("hi there", cipher.Decrypt(secondEncrypted));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsInsteadOfReturningWrongData()
    {
        var cipher = new ChatContentCipher(TestKey());
        var encrypted = cipher.Encrypt("hello");
        var bytes = Convert.FromBase64String(encrypted["GCM:".Length..]);
        bytes[^1] ^= 0xFF; // flip a bit in the tag
        var tampered = "GCM:" + Convert.ToBase64String(bytes);

        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_ValueWithoutGcmPrefix_ReturnedUnchanged()
    {
        var cipher = new ChatContentCipher(TestKey());

        Assert.Equal("plain legacy text", cipher.Decrypt("plain legacy text"));
    }

    [Fact]
    public void EmptyString_RoundTripsAsEmpty()
    {
        var cipher = new ChatContentCipher(TestKey());

        Assert.Equal("", cipher.Encrypt(""));
        Assert.Equal("", cipher.Decrypt(""));
    }

    [Fact]
    public void Constructor_WrongKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ChatContentCipher(new byte[16]));
    }

    [Fact]
    public void GenerateKey_ProducesCorrectLength()
    {
        Assert.Equal(32, ChatContentCipher.GenerateKey().Length);
    }
}

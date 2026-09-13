// Encrypts the few fields that must not be readable in a raw database dump, which today means bank account
// numbers.
//
// It sits on top of whole-disk encryption rather than replacing it: disk encryption protects a stolen
// drive, and this protects a dump taken by somebody who was allowed to connect.
//
// One blob per field, packing the nonce, the authentication tag and the ciphertext together, so an
// encrypted field needs no extra columns beside it.
//
//
// WHAT PRODUCTION STILL NEEDS
//
// The key here is a single symmetric key read from configuration, and in local development an ephemeral
// one is generated when none is set, the same pattern the token signing key uses.
//
// A real deployment must supply a key from a managed key service through a secrets manager rather than a
// configuration string, and should support rotation: re-encrypt on the next write while still accepting
// the old key for reads. This version does not do that, which is recorded rather than implied.

namespace MotsSupplierPortal.Infrastructure.Security;

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

public sealed class FieldEncryptionService
{
    private readonly byte[] _key;

    public FieldEncryptionService(IConfiguration configuration)
    {
        var configuredKey = configuration["FieldEncryption:DataKeyBase64"];
        _key = string.IsNullOrWhiteSpace(configuredKey)
            ? RandomNumberGenerator.GetBytes(32)
            : Convert.FromBase64String(configuredKey);
    }

    public byte[] Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, packed, nonce.Length + tag.Length, ciphertext.Length);
        return packed;
    }

    public string Decrypt(byte[] packed)
    {
        var nonce = packed[..12];
        var tag = packed[12..28];
        var ciphertext = packed[28..];
        var plaintextBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }

    public static string Mask(string accountNumber) =>
        accountNumber.Length <= 4 ? new string('*', accountNumber.Length) : new string('*', accountNumber.Length - 4) + accountNumber[^4..];
}

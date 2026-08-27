using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace MotsSupplierPortal.Infrastructure.Security;

/// <summary>
/// SECURITY-ARCHITECTURE.md §3.4: application-level field encryption for high-sensitivity fields
/// (bank account numbers) via a KMS-backed data key [ASSUMPTION] - so a raw DB dump doesn't expose
/// them, on top of at-rest disk encryption. AES-256-GCM: nonce (12B) + tag (16B) + ciphertext
/// packed into one blob, so no separate columns needed per encrypted field.
///
/// [ASSUMPTION] the data key here is a single symmetric key read from config (dev: generated
/// ephemeral if unset, same pattern as JwtSigningKeyProvider) - production must supply a real
/// KMS-issued key via secrets manager, not a config string, and should support key rotation
/// (re-encrypt on next write, tolerate old key for reads) which this minimal version doesn't yet.
/// </summary>
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

// Encrypting a credential that has to live in the database.
//
// WHY NOT ASP.NET DATA PROTECTION, WHICH IS THE OBVIOUS ANSWER. It is not configured in this product, so its keys
// default to a folder on the machine. That is fine on a developer's laptop and ephemeral in a container: deploy a
// new version and the stored secret can no longer be decrypted, with an error that looks nothing like its cause.
// Making it safe means persisting its keys somewhere, which is a package this product does not have.
//
// SO ONE KEY LIVES IN DEPLOYMENT CONFIGURATION and everything else moves to the database. That keeps the point of
// the screen - an address or a credential can change without a redeploy - while leaving exactly one value in the
// deployment's own store, which never changes and is not something an administrator edits.
//
// AES-GCM RATHER THAN AES-CBC, because it authenticates as well as encrypts. A ciphertext somebody has altered
// fails to decrypt instead of decrypting into something else, and "something else" sent as a credential to
// another ministry's server is not a failure mode worth having.
//
// THE NONCE IS RANDOM PER ENCRYPTION AND STORED BESIDE THE CIPHERTEXT. Reusing a nonce with the same key is the
// one thing that breaks GCM completely, which is why it is generated here rather than derived from anything.
//
// A MISSING OR MALFORMED KEY THROWS AT THE POINT OF USE, naming the setting. The alternative - falling back to
// storing the secret unencrypted - is the kind of helpful default that turns a configuration mistake into a
// database full of plain-text credentials nobody knows about.

namespace MotsSupplierPortal.Infrastructure.Integration;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

public sealed class SecretCipher(IConfiguration configuration)
{
    public const string KeySetting = "Integrations:EncryptionKey";

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public string Protect(string plaintext)
    {
        var key = Key();
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(cipher)}.{Convert.ToBase64String(tag)}";
    }

    public string Unprotect(string protectedValue)
    {
        var parts = protectedValue.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("The stored secret is not in the expected form.");
        }

        var key = Key();
        var nonce = Convert.FromBase64String(parts[0]);
        var cipher = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] Key()
    {
        var configured = configuration[KeySetting];

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"No encryption key is configured: set {KeySetting} to a base64 32-byte value.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{KeySetting} is not valid base64.");
        }

        return key.Length == 32
            ? key
            : throw new InvalidOperationException($"{KeySetting} must decode to 32 bytes, not {key.Length}.");
    }
}

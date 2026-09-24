// Making an API key's secret, and checking one that comes back.
//
// THE PRESENTED FORM IS mots_<prefix>_<secret>, one string the caller copies once and puts in a header. It
// carries its own lookup key, so verification is an indexed read of one row rather than hashing every key in
// the table against the candidate - which is what a secret-only format would force, and which gets slower with
// every key ever issued.
//
// A PLAIN SHA-256 RATHER THAN A PASSWORD KDF, deliberately, and this is the decision most likely to be
// questioned. PBKDF2 and Argon2 exist to make guessing cheap secrets expensive: they buy time against a
// dictionary. There is no dictionary here - the secret is 32 bytes from the system's cryptographic generator,
// and no amount of hardware searches 2^256. What a slow hash would buy instead is a cost paid on every single
// request by a nightly job that may pull a hundred pages. The salt is still per key, so two keys that somehow
// held the same secret would not hash alike, and the table gives no help to anyone comparing hashes across
// rows.
//
// THE COMPARISON IS FIXED-TIME, which is the part that actually matters. An ordinary string or array equality
// returns as soon as it finds a difference, and the time it took is a measurement of how many leading bytes
// were right - enough, over many attempts, to recover a secret a byte at a time. CryptographicOperations
// .FixedTimeEquals does not stop early.
//
// BASE64URL AND NOT BASE64, because this value travels in an HTTP header and gets pasted into shells and config
// files by people. '+' and '/' survive all of that unevenly; '-' and '_' do not need escaping anywhere.
//
// THE PREFIX IS LETTERS AND DIGITS ONLY, AND THAT IS A BUG FIX. It was cut from base64url like the secret, and
// base64url contains the underscore this format separates on - so about one key in eight was issued with an
// underscore inside its prefix, the separator landed in the wrong place, and the key could not be parsed back.
// It authenticated nothing and the only symptom was a 401 nobody could reproduce. A generator whose output
// sometimes collides with its own delimiter is the defect; narrowing the alphabet is the fix, and the parse test
// over many generations is what keeps it fixed. 36 characters over 8 positions is still far more than the number
// of keys this product will ever hold, and the database's unique index is the backstop.
//
// THE SECRET IS RETURNED ONCE AND NEVER STORED. What the caller gets back is the only copy; the database holds
// the salt and the hash. That is not an inconvenience to be worked around later - it is the property that makes
// a leaked key replaceable rather than a thing to be looked up and re-sent.

namespace MotsSupplierPortal.Infrastructure.Integration;

using System.Security.Cryptography;
using System.Text;
using MotsSupplierPortal.Domain.Integration;

public sealed record GeneratedApiKeySecret(string Prefix, string Salt, string SecretHash, string Presented);

public static class ApiKeySecret
{
    public const string PresentedPrefix = "mots_";

    private const int SecretBytes = 32;
    private const int SaltBytes = 16;

    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static GeneratedApiKeySecret Generate()
    {
        var prefix = RandomToken(ApiKey.PrefixLength);
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var salt = Base64Url(RandomNumberGenerator.GetBytes(SaltBytes));

        return new GeneratedApiKeySecret(
            prefix,
            salt,
            Hash(secret, salt),
            $"{PresentedPrefix}{prefix}_{secret}");
    }

    public static bool TryParse(string presented, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;

        if (!presented.StartsWith(PresentedPrefix, StringComparison.Ordinal)) return false;

        var body = presented[PresentedPrefix.Length..];
        var separator = body.IndexOf('_', StringComparison.Ordinal);
        if (separator != ApiKey.PrefixLength) return false;

        prefix = body[..separator];
        secret = body[(separator + 1)..];

        return secret.Length > 0;
    }

    public static bool Verify(string secret, string salt, string expectedHash)
    {
        var candidate = Encoding.UTF8.GetBytes(Hash(secret, salt));
        var expected = Encoding.UTF8.GetBytes(expectedHash);

        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    private static string Hash(string secret, string salt)
        => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{secret}")));

    private static string RandomToken(int length)
        => RandomNumberGenerator.GetString(PrefixAlphabet, length);

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

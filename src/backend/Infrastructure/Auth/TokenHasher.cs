// Generating opaque tokens, and hashing them for storage.
//
// Refresh tokens and single-use links are stored hashed and never in plain text, so a stolen database yields
// nothing that can be presented.
//
// The generated form is URL-safe, because these values travel in links and cookies.

namespace MotsSupplierPortal.Infrastructure.Auth;

using System.Security.Cryptography;
using System.Text;

public static class TokenHasher
{
    public static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

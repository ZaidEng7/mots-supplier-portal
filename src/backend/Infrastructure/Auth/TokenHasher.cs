using System.Security.Cryptography;
using System.Text;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>Refresh tokens are stored hashed, never in plaintext (STORY-01.1.1 DoD).</summary>
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

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Holds the single RSA keypair this instance signs/validates JWTs with (SECURITY-ARCHITECTURE.md
/// §1.1: RS256). Registered as a singleton so the same key services both the signing side
/// (JwtTokenService) and the validation side (Program.cs's JwtBearerOptions) within this process.
/// </summary>
public sealed class JwtSigningKeyProvider : IDisposable
{
    private readonly RSA _rsa;

    public JwtSigningKeyProvider(IOptions<JwtOptions> options)
    {
        _rsa = RSA.Create(2048);
        var pem = options.Value.RsaPrivateKeyPem;
        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa.ImportFromPem(pem);
        }
        // else: RSA.Create(2048) already generated a fresh random keypair - fine for dev, where
        // restarting the process invalidating old tokens is an acceptable trade-off; see the
        // [ASSUMPTION] on JwtOptions.RsaPrivateKeyPem for production.
    }

    /// <summary>Full keypair - signing only.</summary>
    public RsaSecurityKey GetSigningKey() => new(_rsa);

    /// <summary>Public parameters only - validation must never hold the private key.</summary>
    public RsaSecurityKey GetValidationKey() => new(_rsa.ExportParameters(includePrivateParameters: false));

    public void Dispose() => _rsa.Dispose();
}

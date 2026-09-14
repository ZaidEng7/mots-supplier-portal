// The single signing keypair this process uses for access tokens.
//
// A singleton, so the same key serves both the signing side and the validation side within this process.
//
// With no key configured it generates a fresh random one at startup. That is fine for development, where
// restarting the process invalidating old tokens is an acceptable trade; the configuration option's own note
// records the assumption for production.
//
// The validation key exports the public parameters only, because the validation side must never hold the
// private key.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
    }

    public RsaSecurityKey GetSigningKey() => new(_rsa);

    public RsaSecurityKey GetValidationKey() => new(_rsa.ExportParameters(includePrivateParameters: false));

    public void Dispose() => _rsa.Dispose();
}

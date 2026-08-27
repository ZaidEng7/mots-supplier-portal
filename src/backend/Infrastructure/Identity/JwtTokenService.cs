using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>SECURITY-ARCHITECTURE.md §1.1 token-contents table: sub, roles, perms (compact
/// permission set), supplierId?, orgId?, scope, amr, jti, iat/exp, iss, aud - signed RS256.</summary>
public sealed class JwtTokenService(JwtSigningKeyProvider signingKeyProvider, IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenResult IssueAccessToken(
        Guid userId,
        string email,
        Guid? supplierId,
        Guid? organizationId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        IReadOnlyList<string> amr)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("scope", string.Join(' ', permissions)),
        ];

        if (supplierId is not null) claims.Add(new Claim("supplierId", supplierId.Value.ToString()));
        if (organizationId is not null) claims.Add(new Claim("organizationId", organizationId.Value.ToString()));
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        claims.AddRange(permissions.Select(p => new Claim("perms", p)));
        claims.AddRange(amr.Select(a => new Claim("amr", a)));

        var creds = new SigningCredentials(signingKeyProvider.GetSigningKey(), SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

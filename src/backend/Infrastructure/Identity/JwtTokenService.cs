using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>SECURITY-ARCHITECTURE.md §1.1 token-contents table: sub, roles, perms (compact
/// permission set), supplierId?, orgId?, amr, jti, iat/exp, iss, aud - signed RS256.
///
/// <para>Task #18/MSP-92: used to also emit a space-joined "scope" claim carrying the same
/// permissions as "perms", one per Claim. Confirmed by grep across backend, frontend, and this
/// doc's own token-contents table that nothing ever read "scope" - PermissionEndpointFilter reads
/// "perms" exclusively (see the roles-claim warning below for what happens when a claim looks
/// read but isn't). Two representations of one fact, the exact pattern already responsible for
/// four defects in this codebase; removed rather than kept as a second source that could drift
/// from "perms" the same way the reviewer-flag/DTO-key vocabularies did (MSP-85).</para>
/// </summary>
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
        ];

        if (supplierId is not null) claims.Add(new Claim("supplierId", supplierId.Value.ToString()));
        if (organizationId is not null) claims.Add(new Claim("organizationId", organizationId.Value.ToString()));
        // WARNING FOR ANYONE ADDING A ROLE CHECK: these go into a CUSTOM "roles" claim, and
        // TokenValidationParameters.RoleClaimType is deliberately not set (authorization here is
        // permission-based - see PermissionEndpointFilter, which reads "perms").
        //
        // The consequence is that ClaimsPrincipal.IsInRole() matches NOTHING and returns false for
        // every user, including system_admin. It compiles, it reads correctly, and it silently
        // denies everyone - which looks like a working guard if you only check that an
        // unauthorised caller is refused. That nearly shipped in the Hangfire dashboard filter
        // (MSP-87); read the roles claim directly instead, as that filter now does.
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

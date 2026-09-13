// Minting the signed access token, and what it carries.
//
// The written security architecture fixes the contents: the subject, the roles, the compact permission set, the
// company or the organization where there is one, how the user authenticated, a token identifier, the issue and
// expiry times, the issuer and the audience, signed asymmetrically.
//
//
// WARNING FOR ANYONE ADDING A ROLE CHECK
//
// The roles go into a CUSTOM claim, and the framework's role-claim setting is deliberately not pointed at it,
// because authorisation here is permission-based.
//
// The consequence is that the framework's own is-in-role test matches NOTHING and returns false for every
// user, including the system administrator. It compiles, it reads correctly, and it silently denies everyone,
// which looks like a working guard if you only check that an unauthorised caller is refused.
//
// That nearly shipped in the background-jobs dashboard filter. Read the roles claim directly instead, as that
// filter now does.
//
//
// ONE REPRESENTATION OF THE PERMISSIONS, NOT TWO
//
// The token used to also carry a space-joined list of the same permissions under a second name.
//
// Nothing ever read it: the route filter reads the compact set exclusively, which was confirmed by searching
// the backend, the frontend and the architecture document's own table of token contents rather than assumed.
//
// Two representations of one fact is the exact pattern already responsible for four defects here, so it was
// removed rather than kept as a second source that could drift from the first the way other duplicated
// vocabularies did.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Application.Common;

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

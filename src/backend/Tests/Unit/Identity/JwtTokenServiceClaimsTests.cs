using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Tests.Unit.Identity;

/// <summary>
/// Task #18/MSP-92: JwtTokenService used to emit permissions twice - a space-joined "scope" claim
/// alongside individual "perms" claims - and only "perms" was ever read anywhere (confirmed by
/// grep across backend, frontend, and SECURITY-ARCHITECTURE.md's own token-contents table before
/// removing it, in case an external consumer or spec expected the OAuth2-conventional "scope"
/// claim name; none was found). Two representations of one fact is the exact pattern this session
/// has already found responsible for four other defects - removed rather than kept.
/// </summary>
public sealed class JwtTokenServiceClaimsTests
{
    private static JwtTokenService BuildService()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
        });
        var signingKeyProvider = new JwtSigningKeyProvider(options);
        return new JwtTokenService(signingKeyProvider, options);
    }

    [Fact]
    public void The_token_carries_perms_but_no_scope_claim()
    {
        var service = BuildService();

        var result = service.IssueAccessToken(
            userId: Guid.NewGuid(),
            email: "probe@example.com",
            supplierId: null,
            organizationId: null,
            roles: ["supplier_admin"],
            permissions: ["supplier.edit", "supplier.submit"],
            amr: ["pwd"]);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        var permsClaims = token.Claims.Where(c => c.Type == "perms").Select(c => c.Value).ToList();
        var scopeClaims = token.Claims.Where(c => c.Type == "scope").ToList();

        permsClaims.Should().BeEquivalentTo(["supplier.edit", "supplier.submit"],
            "perms is the claim PermissionEndpointFilter actually reads - it must still carry every permission");
        scopeClaims.Should().BeEmpty(
            "scope carried the same data as perms and nothing ever read it - a second representation of the same fact");
    }
}

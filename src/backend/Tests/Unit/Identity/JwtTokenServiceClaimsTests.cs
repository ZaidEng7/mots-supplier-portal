// The access token carries the permissions once, not twice.
//
// It used to emit them a second time under a space-joined claim alongside the individual ones, and only the
// individual ones were ever read.
//
// That was confirmed by searching the backend, the frontend, and the security architecture's own table of token
// contents before removing it, in case an external consumer or a specification expected the conventional name for
// the joined form. None was found.
//
// Two representations of one fact is the exact pattern already responsible for four other defects in this
// codebase, so it was removed rather than kept.

namespace MotsSupplierPortal.Tests.Unit.Identity;

using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Identity;

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

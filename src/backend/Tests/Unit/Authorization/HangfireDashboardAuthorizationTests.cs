using System.Security.Claims;
using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Unit.Authorization;

/// <summary>
/// MSP-87: the Hangfire dashboard must admit system_admin and nobody else.
///
/// <para><b>Why the positive case is the one that matters.</b> Verifying only that a supplier is
/// refused cannot distinguish "correctly restricted" from "denies everyone" - the 403 is identical
/// either way. That is not hypothetical: the first version of this filter called
/// <c>user.IsInRole()</c>, and this API issues roles in a custom "roles" claim without setting
/// TokenValidationParameters.RoleClaimType, so IsInRole matched nothing and would have locked out
/// system_admin as well. The live check showed a supplier getting 403 and looked like success.</para>
/// </summary>
public sealed class HangfireDashboardAuthorizationTests
{
    private static bool Authorize(params Claim[] claims) =>
        HangfireDashboardAuthorization.IsAuthorized(
            new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")));

    [Fact]
    public void A_system_admin_is_admitted()
    {
        // The assertion that proves the filter restricts rather than merely refuses.
        Authorize(new Claim("roles", Roles.SystemAdmin)).Should().BeTrue();
    }

    [Theory]
    [InlineData("supplier_admin")]
    [InlineData("onboarding_reviewer")]
    [InlineData("procurement_manager")]
    [InlineData("ministry_viewer")]
    public void Every_other_role_is_refused(string role)
    {
        // supplier_admin is the one confirmed to have read 15 other suppliers' email addresses and
        // live verification tokens through this dashboard. The rest are listed because "authenticated
        // staff" was never the requirement - FR-ADM-009 names system_admin specifically.
        Authorize(new Claim("roles", role)).Should().BeFalse();
    }

    [Fact]
    public void An_authenticated_user_with_no_roles_is_refused()
    {
        Authorize(new Claim("sub", Guid.NewGuid().ToString())).Should().BeFalse();
    }

    [Fact]
    public void A_role_claim_under_the_wrong_type_is_not_accepted()
    {
        // ClaimTypes.Role is the standard type this API deliberately does not use. Accepting it
        // would mean a token shaped by some other issuer could satisfy the filter.
        Authorize(new Claim(ClaimTypes.Role, Roles.SystemAdmin)).Should().BeFalse();
    }
}

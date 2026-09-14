// The background-jobs dashboard must admit the system administrator and nobody else.
//
//
// WHY THE POSITIVE CASE IS THE ONE THAT MATTERS
//
// Verifying only that a supplier is refused cannot distinguish "correctly restricted" from "denies everyone",
// because the refusal is identical either way.
//
// That is not hypothetical. The first version of this filter used the framework's own is-in-role test, and this
// application issues roles in a custom claim without pointing the framework's role-claim setting at it, so that
// test matched nothing and would have locked out the administrator as well. The live check showed a supplier
// being refused and looked like success.
//
//
// WHO IS LISTED, AND WHY THE STANDARD CLAIM TYPE IS REFUSED
//
// The supplier administrator is the role confirmed to have read fifteen other suppliers' email addresses and
// live verification tokens through this dashboard. The rest are listed because "authenticated staff" was never
// the requirement: the written requirement names the system administrator specifically.
//
// And the standard role claim type, which this application deliberately does not use, is refused: accepting it
// would mean a token shaped by some other issuer could satisfy the filter.

namespace MotsSupplierPortal.Tests.Unit.Authorization;

using System.Security.Claims;
using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Domain.Identity;

public sealed class HangfireDashboardAuthorizationTests
{
    private static bool Authorize(params Claim[] claims) =>
        HangfireDashboardAuthorization.IsAuthorized(
            new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")));

    [Fact]
    public void A_system_admin_is_admitted()
    {
        Authorize(new Claim("roles", Roles.SystemAdmin)).Should().BeTrue();
    }

    [Theory]
    [InlineData("supplier_admin")]
    [InlineData("onboarding_reviewer")]
    [InlineData("procurement_manager")]
    [InlineData("ministry_viewer")]
    public void Every_other_role_is_refused(string role)
    {
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
        Authorize(new Claim(ClaimTypes.Role, Roles.SystemAdmin)).Should().BeFalse();
    }
}

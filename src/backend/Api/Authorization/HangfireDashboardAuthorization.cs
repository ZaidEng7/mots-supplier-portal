using System.Security.Claims;
using Hangfire.Dashboard;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Restricts the Hangfire dashboard to system administrators (FR-ADM-009, MSP-87).
///
/// <para><b>Why a filter and not the fallback policy.</b> The dashboard was mapped with no filter at
/// all, so the application's deny-by-default <c>FallbackPolicy</c> was the only gate — and that
/// requires <i>an authenticated user</i>, not a role. Any supplier_admin could open the dashboard.
/// The floor did real work (anonymous access was closed) and that partial success is exactly what
/// made the remainder invisible: nothing failed, nothing lied, the surface simply read as handled
/// because it was handled — just not to the depth required.</para>
///
/// <para><b>What was reachable.</b> Sampling the 25 most recent jobs with one supplier's token
/// returned 15 distinct email addresses belonging to other suppliers, and job arguments include
/// verification, password-reset and invite URLs with their tokens in plaintext. Forgot-password is
/// anonymous, so the chain was: request a reset for any account, read the token from the dashboard,
/// take the account. This filter closes that.</para>
///
/// <para>Keeping those tokens out of the job store entirely is the durable fix and is tracked
/// separately — a filter is a rule, an absent value is a fact. This is the rule; the fact still
/// needs doing.</para>
/// </summary>
public sealed class HangfireDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => IsAuthorized(context.GetHttpContext().User);

    /// <summary>
    /// The decision, separated from Hangfire's DashboardContext so it can be asserted directly.
    /// Faking that context requires a storage provider and is more machinery than the rule
    /// deserves - and the rule is the part worth testing.
    /// </summary>
    public static bool IsAuthorized(ClaimsPrincipal user)
    {
        // Reads the "roles" claim directly rather than calling IsInRole, and that is not a style
        // choice. This API issues roles in a custom "roles" claim and never sets
        // TokenValidationParameters.RoleClaimType, so IsInRole matches nothing and returns false
        // for EVERY user - including system_admin. A filter written that way denies the whole
        // world, which looks like success when you only test that the unauthorised case is
        // refused: the supplier's 403 is identical either way.
        //
        // Same shape as PermissionEndpointFilter, which reads the "perms" claim directly for the
        // same reason.
        return user.Identity?.IsAuthenticated == true
            && user.Claims.Any(c => c.Type == "roles" && c.Value == Roles.SystemAdmin);
    }
}

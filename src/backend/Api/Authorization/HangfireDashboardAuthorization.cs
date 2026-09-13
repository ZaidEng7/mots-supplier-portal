// Restricts the background-jobs dashboard to system administrators.
//
// The dashboard used to be mapped with no filter at all, so the application's deny-anonymous floor was
// the only gate, and that requires a signed-in user rather than a particular role. Any supplier's own
// administrator could open it.
//
// The floor did real work, since anonymous access was closed, and that partial success is exactly what
// made the rest invisible. Nothing failed and nothing lied; the surface read as handled because it was
// handled, just not to the depth required.
//
// What was reachable: sampling the twenty-five most recent jobs with one supplier's token returned
// fifteen email addresses belonging to other suppliers, and job arguments include verification,
// password-reset and invitation links with their tokens in plain text. Asking for a password reset
// needs no sign-in, so the chain was to request a reset for any account, read the token off the
// dashboard, and take the account.
//
// Keeping those tokens out of the job store in the first place is the durable fix and is tracked
// separately. A filter is a rule; an absent value is a fact. This is the rule.
//
// IsAuthorized is separated from the dashboard's own context so it can be asserted directly. Faking
// that context needs a storage provider, which is more machinery than the rule deserves, and the rule
// is the part worth testing.
//
// It reads the roles claim directly rather than asking the framework whether the user is in a role,
// and that is not a style choice. This API issues roles in its own claim and never tells the framework
// which claim holds them, so the framework's check matches nothing and answers false for every user,
// including a system administrator. A filter written that way denies the whole world, which looks like
// success if the only thing tested is that an unauthorised caller is refused: the supplier's refusal
// is identical either way. The permission filter reads its own claim directly for the same reason.

namespace MotsSupplierPortal.Api.Authorization;

using System.Security.Claims;
using Hangfire.Dashboard;
using MotsSupplierPortal.Domain.Identity;

public sealed class HangfireDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => IsAuthorized(context.GetHttpContext().User);

    public static bool IsAuthorized(ClaimsPrincipal user)
    {
        return user.Identity?.IsAuthenticated == true
            && user.Claims.Any(c => c.Type == "roles" && c.Value == Roles.SystemAdmin);
    }
}

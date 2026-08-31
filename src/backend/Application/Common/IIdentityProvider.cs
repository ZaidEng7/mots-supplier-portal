using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Application.Common;

/// <summary>Result of a password sign-in attempt. Deliberately its own type rather than
/// Microsoft.AspNetCore.Identity.SignInResult: leaking an ASP.NET Core Identity type into this
/// interface would defeat the point of the seam (FR-IAM-011 - "swappable to an external IdP...
/// without changing authorization semantics") by tying every caller back to the concrete provider
/// this interface exists to hide.</summary>
public sealed record IdentitySignInResult(bool Succeeded, bool IsLockedOut)
{
    public static readonly IdentitySignInResult Success = new(true, false);
    public static readonly IdentitySignInResult Failed = new(false, false);
    public static readonly IdentitySignInResult LockedOut = new(false, true);
}

/// <summary>
/// Task #7/Stage D: the identity-verification seam FR-IAM-011 asks for - "Identity provider is
/// swappable to an external IdP (Keycloak/Entra) without changing authorization semantics",
/// Priority C ("Could-have"), and the foundational decision's own framing is "local identity
/// now... swappable for external IdP later". Both mean: build the abstraction point now, not a
/// live Keycloak/Entra integration - <see cref="AspNetIdentityProvider"/> (Infrastructure/Identity)
/// is the ONLY implementation, and does exactly what LoginHandler did before this stage existed.
///
/// Scoped narrowly to what actually verifies identity, matching what a real external-IdP swap
/// would take over first - not every ASP.NET Core Identity touch point in the codebase.
/// Registration, password reset, MFA enrollment, team-invite user creation, and session/refresh-
/// token management still call UserManager/SignInManager directly - deliberately out of this
/// pass (the ticket's own scope: "purely about the login/authentication call path").
/// </summary>
public interface IIdentityProvider
{
    Task<AppUser?> FindByEmailAsync(string email);
    Task<IdentitySignInResult> CheckPasswordSignInAsync(AppUser user, string password, bool lockoutOnFailure);
    Task<IReadOnlyList<string>> GetRolesAsync(AppUser user);
    Task<bool> VerifyTwoFactorTokenAsync(AppUser user, string code);
    Task<bool> RedeemTwoFactorRecoveryCodeAsync(AppUser user, string code);
}

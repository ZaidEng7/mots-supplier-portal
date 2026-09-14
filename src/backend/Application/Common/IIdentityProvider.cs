// The seam that verifies who somebody is, so a real external identity provider could replace it later
// without changing anything about what callers are allowed to do.
//
// There is one implementation, and it does exactly what the sign-in handler did before this seam existed.
// The point is the abstraction point rather than a live integration with anything.
//
// The sign-in outcome is its own type rather than the identity framework's. Letting that type through here
// would defeat the whole purpose, by tying every caller back to the concrete provider this interface exists
// to hide.
//
// It is scoped narrowly to what actually verifies identity, which is what a real replacement would take
// over first. Registration, password reset, second-factor enrolment, creating an invited user, and session
// and refresh-token management all still call the framework directly, deliberately outside this seam.

namespace MotsSupplierPortal.Application.Common;

using MotsSupplierPortal.Domain.Identity;

public sealed record IdentitySignInResult(bool Succeeded, bool IsLockedOut)
{
    public static readonly IdentitySignInResult Success = new(true, false);
    public static readonly IdentitySignInResult Failed = new(false, false);
    public static readonly IdentitySignInResult LockedOut = new(false, true);
}

public interface IIdentityProvider
{
    Task<AppUser?> FindByEmailAsync(string email);
    Task<IdentitySignInResult> CheckPasswordSignInAsync(AppUser user, string password, bool lockoutOnFailure);
    Task<IReadOnlyList<string>> GetRolesAsync(AppUser user);
    Task<bool> VerifyTwoFactorTokenAsync(AppUser user, string code);
    Task<bool> RedeemTwoFactorRecoveryCodeAsync(AppUser user, string code);
}

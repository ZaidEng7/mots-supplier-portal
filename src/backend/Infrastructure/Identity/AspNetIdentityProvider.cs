using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Task #7/Stage D: the only IIdentityProvider implementation - delegates straight to
/// ASP.NET Core Identity, exactly what LoginHandler called directly before this stage. This class
/// existing and nothing else changing behaviorally is the point: the seam is inserted, not yet
/// used to swap anything.</summary>
public sealed class AspNetIdentityProvider(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager) : IIdentityProvider
{
    public Task<AppUser?> FindByEmailAsync(string email) => userManager.FindByEmailAsync(email);

    public async Task<IdentitySignInResult> CheckPasswordSignInAsync(AppUser user, string password, bool lockoutOnFailure)
    {
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
        return new IdentitySignInResult(result.Succeeded, result.IsLockedOut);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(AppUser user) => (IReadOnlyList<string>)await userManager.GetRolesAsync(user);

    public Task<bool> VerifyTwoFactorTokenAsync(AppUser user, string code) =>
        userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);

    public async Task<bool> RedeemTwoFactorRecoveryCodeAsync(AppUser user, string code) =>
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;
}

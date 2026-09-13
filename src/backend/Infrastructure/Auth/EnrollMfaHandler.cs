using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// STORY-01.5.1: TOTP enrollment scaffold, gated by Mfa:Enabled (docs/product/ASSUMPTIONS.md
/// ASM-081 - available, not globally mandatory in v1). Per-role enforcement is a later policy
/// extension; this delivers enroll -> confirm -> recovery codes.
/// </summary>
public sealed class EnrollMfaHandler(UserManager<AppUser> userManager) : IEnrollMfaHandler
{
    private const string Issuer = "MOTS Supplier Portal";

    public async Task<EnrollMfaResult> HandleAsync(EnrollMfaCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email!)}" +
                  $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

        return new EnrollMfaResult(key!, uri);
    }
}

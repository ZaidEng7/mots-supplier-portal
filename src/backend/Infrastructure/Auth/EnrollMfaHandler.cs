// Starting a second-factor enrolment: returning the shared secret and the URL an authenticator app scans.
//
// An existing key is reused rather than regenerated, so opening the screen twice does not invalidate a code the
// user has already scanned.
//
// This is the first half of enrol, confirm, recovery codes. Whether a role must carry a second factor is a
// policy decision enforced at sign-in rather than here.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

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

// Confirming a second-factor enrolment with a code from the authenticator, and issuing recovery codes.
//
// The framework persists the enrolment itself, but the audit row is on this context and the logger no longer
// saves, so without the save here the enrolment would succeed while leaving no record that it happened.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ConfirmMfaEnrollmentHandler(
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    AppDbContext db) : IConfirmMfaEnrollmentHandler
{
    public async Task<ConfirmMfaEnrollmentResult> HandleAsync(ConfirmMfaEnrollmentCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, command.Code);

        if (!isValid)
        {
            return new ConfirmMfaEnrollmentResult.InvalidCode();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        await auditLogger.LogAsync("User", user.Id, "mfa_enrolled", user.Id, user.FullName, ct: ct);
        await db.SaveChangesAsync(ct);

        return new ConfirmMfaEnrollmentResult.Success([.. recoveryCodes ?? []]);
    }
}

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

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
        // MSP-64: UserManager persists the enrollment itself, but the audit row is on the
        // AppDbContext and AuditLogger no longer saves. Without this, MFA enrollment would
        // succeed while leaving no record that it happened.
        await db.SaveChangesAsync(ct);

        return new ConfirmMfaEnrollmentResult.Success([.. recoveryCodes ?? []]);
    }
}

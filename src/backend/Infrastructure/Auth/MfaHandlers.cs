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

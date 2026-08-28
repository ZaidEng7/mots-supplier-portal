using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// STORY-02.2.1: the opaque link token (SecurityTokenService) is the sole lookup key - single-use,
/// 24h TTL, hashed, resolves the user without a userId ever appearing in the URL
/// (SECURITY-ARCHITECTURE.md §1.6). Once resolved, an Identity email-confirmation token is
/// generated and consumed internally, server-side only, to perform the actual state change.
/// </summary>
public sealed class VerifyEmailHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISecurityTokenService securityTokenService,
    IAuditLogger auditLogger) : IVerifyEmailHandler
{
    public async Task<VerifyEmailResult> HandleAsync(VerifyEmailCommand command, CancellationToken ct)
    {
        var consumed = await securityTokenService.ConsumeAsync(command.Token, SecurityTokenPurpose.EmailVerification, ct);
        if (consumed is not ConsumeSecurityTokenResult.Success success)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        var user = await userManager.FindByIdAsync(success.UserId.ToString());
        if (user is null || user.SupplierId is null)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        var identityToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmResult = await userManager.ConfirmEmailAsync(user, identityToken);
        if (!confirmResult.Succeeded)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        var supplier = await db.Suppliers
            .Include(s => s.Representatives)
            .FirstOrDefaultAsync(s => s.Id == user.SupplierId, ct);

        if (supplier is null)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        try
        {
            supplier.MarkEmailVerified();
        }
        catch (DomainException)
        {
            // Already verified via a different (still-valid) token issued earlier - idempotent,
            // not an error. The opaque token itself can never be replayed (ConsumeAsync is
            // single-use), so this only fires if the account somehow had two live tokens.
            return new VerifyEmailResult.Success();
        }

        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync(
            aggregateType: "Supplier",
            aggregateId: supplier.Id,
            action: "state_change",
            actorUserId: user.Id,
            actorLabel: user.FullName,
            fromState: nameof(SupplierOnboardingState.Draft),
            toState: nameof(SupplierOnboardingState.EmailVerified),
            referenceCode: supplier.ReferenceCode,
            ct: ct);

        return new VerifyEmailResult.Success();
    }
}

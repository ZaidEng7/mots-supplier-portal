// Confirming an email address from the link.
//
// The opaque link token is the only lookup key: single-use, short-lived, stored hashed, and it resolves the user
// without an identifier ever appearing in the URL.
//
// Once resolved, a framework confirmation token is generated and consumed internally, on the server only, to
// perform the actual change.
//
// An address already verified is idempotent rather than an error. The opaque token itself can never be replayed,
// because consuming it is single-use, so that only happens if the account somehow had two live tokens.

namespace MotsSupplierPortal.Infrastructure.Registrations;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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

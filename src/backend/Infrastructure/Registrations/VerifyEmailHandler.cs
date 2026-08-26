using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// STORY-02.2.1: single-use, time-limited token (Identity's DataProtector email-confirmation
/// provider) transitions Draft -> EmailVerified. Domain refuses if not currently Draft.
/// </summary>
public sealed class VerifyEmailHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger) : IVerifyEmailHandler
{
    public async Task<VerifyEmailResult> HandleAsync(VerifyEmailCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null || user.SupplierId is null)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        string decodedToken;
        try
        {
            decodedToken = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
        }
        catch (FormatException)
        {
            return new VerifyEmailResult.InvalidOrExpiredToken();
        }

        var confirmResult = await userManager.ConfirmEmailAsync(user, decodedToken);
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
            // Already verified (token replayed after success) - idempotent no-op, not an error.
            return new VerifyEmailResult.Success();
        }

        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync(
            aggregateType: "Supplier",
            aggregateId: supplier.Id,
            action: "state_change",
            correlationId: Guid.NewGuid(),
            actorUserId: user.Id,
            actorLabel: user.FullName,
            fromState: nameof(SupplierOnboardingState.Draft),
            toState: nameof(SupplierOnboardingState.EmailVerified),
            referenceCode: supplier.ReferenceCode,
            ct: ct);

        return new VerifyEmailResult.Success();
    }
}

using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-03.6 [ASSUMPTION]: manual self-claim, not round-robin/manager-assigned - see
/// Supplier.AssignedReviewerId's own doc comment for why.</summary>
public sealed class ClaimReviewItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IClaimReviewItemHandler
{
    public async Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ClaimQueueItemResult.NotFound();

        var previousReviewerId = supplier.AssignedReviewerId;
        supplier.AssignReviewer(scope.UserId!.Value);

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_claimed", scope.UserId,
            fromState: previousReviewerId?.ToString(), toState: scope.UserId.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var reviewerName = (await db.Users.Where(u => u.Id == scope.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct));
        return new ClaimQueueItemResult.Success(new ReviewQueueItemDto(
            supplier.ReferenceCode, supplier.DisplayNameAr, supplier.DisplayNameEn, supplier.OnboardingState.ToString(),
            supplier.CreatedAt, supplier.AssignedReviewerId, reviewerName));
    }
}

// Releasing a claimed application back into the queue.
//
// The previous reviewer is written onto the audit row, so a release is traceable to whoever held it rather
// than only to whoever let it go.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

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

public sealed class UnassignReviewItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUnassignReviewItemHandler
{
    public async Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ClaimQueueItemResult.NotFound();

        var previousReviewerId = supplier.AssignedReviewerId;
        supplier.UnassignReviewer();

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_unassigned", scope.UserId,
            fromState: previousReviewerId?.ToString(), toState: null, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new ClaimQueueItemResult.Success(new ReviewQueueItemDto(
            supplier.ReferenceCode, supplier.DisplayNameAr, supplier.DisplayNameEn, supplier.OnboardingState.ToString(),
            supplier.CreatedAt, null, null));
    }
}

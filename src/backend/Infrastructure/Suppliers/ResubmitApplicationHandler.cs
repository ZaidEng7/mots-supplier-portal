// A supplier resubmits after a reviewer asked them for more information.
//
// It resubmits and immediately resumes the review, so the application does not sit in a state nobody is
// working, and it closes the open information request.
//
//
// AN EMPTY FLAGGED LIST IS NOT "NO RESTRICTION"
//
// A request with nothing flagged in one of its two dimensions means nothing in that dimension can block the
// resubmission, which is exactly the flagged set when nothing there was flagged.
//
// A missing request falls back to empty on both. It should not happen, because this only runs from the state
// that a request created, and empty is the conservative choice: nothing is exempted from the full check.
//
// The reviewer pool is emailed afterwards, once the change is committed.

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

public sealed class ResubmitApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IResubmitApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ReviewDecisionResult.NotFound();

        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        var activeAnnotation = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == supplier.Id && a.ResolvedAt == null)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);

        var flaggedProfileFields = activeAnnotation?.FlaggedProfileFields ?? [];
        var flaggedDocumentTypeCodes = activeAnnotation is null
            ? []
            : await db.DocumentTypes
                .Where(t => activeAnnotation.FlaggedDocumentTypeIds.Contains(t.Id))
                .Select(t => t.Code)
                .ToListAsync(ct);

        try
        {
            var missingDocs = await DocumentCompletenessEvaluator.GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
            supplier.Resubmit(missingDocs, flaggedProfileFields, flaggedDocumentTypeCodes);
            await auditLogger.LogAsync("Supplier", supplier.Id, "application_resubmitted", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);

            supplier.PickUpForReview();
            await auditLogger.LogAsync("Supplier", supplier.Id, "application_review_resumed", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        }
        catch (DomainException ex)
        {
            return new ReviewDecisionResult.InvalidState(ex.Message);
        }

        if (activeAnnotation is not null)
        {
            activeAnnotation.ResolvedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var reviewerUserIds = await ReviewerNotify.GetReviewerPoolUserIdsAsync(db, ct);
        foreach (var reviewerUserId in reviewerUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendApplicationResubmittedEmailAsync(reviewerUserId, supplier.Id, CancellationToken.None));
        }

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

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

        // Task #32: an annotation with nothing flagged in one of these two dimensions is not "no
        // restriction" - Where(x => [].Contains(x)) correctly yields empty, so an empty array here
        // means nothing in that dimension can block resubmit, which is exactly the flagged set
        // when nothing there was actually flagged. A missing annotation (shouldn't happen - Resubmit
        // only runs from InfoRequested, which only exists because RequestInfo created one) falls
        // back to empty on both, the conservative choice: nothing is exempted from the full check.
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

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
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class ListReviewQueueHandler(AppDbContext db) : IListReviewQueueHandler
{
    public async Task<IReadOnlyList<ReviewQueueItemDto>> HandleAsync(CancellationToken ct)
    {
        var states = new[] { SupplierOnboardingState.Submitted, SupplierOnboardingState.UnderReview, SupplierOnboardingState.InfoRequested };
        return await db.Suppliers
            .Where(s => states.Contains(s.OnboardingState))
            .OrderBy(s => s.ReferenceCode)
            .Select(s => new ReviewQueueItemDto(s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, s.OnboardingState.ToString()))
            .ToListAsync(ct);
    }
}

public sealed class GetReviewerSupplierViewHandler(AppDbContext db) : IGetReviewerSupplierViewHandler
{
    public async Task<ReviewerSupplierViewDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile()
            .FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return null;

        var documents = await ListSupplierDocumentsHandler.BuildAsync(db, supplier.Id, ct);

        var typesById = await db.DocumentTypes.ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        var annotations = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == supplier.Id)
            .OrderByDescending(a => a.RequestedAt)
            .Select(a => new ReviewAnnotationDto(
                a.Id, a.RequestedAt, a.Reason, a.FlaggedProfileFields,
                a.FlaggedDocumentTypeIds.Select(id => typesById.GetValueOrDefault(id, id.ToString())).ToList(),
                a.ResolvedAt))
            .ToListAsync(ct);

        var erpSync = new ErpSyncDto(supplier.ExternalId, supplier.SyncStatus.ToString(), supplier.LastSyncedAt);
        return new ReviewerSupplierViewDto(SupplierDtoMapper.ToDto(supplier), erpSync, documents, annotations);
    }
}

public sealed class GetOwnActiveAnnotationHandler(AppDbContext db, IScopeContext scope) : IGetOwnActiveAnnotationHandler
{
    public async Task<ReviewAnnotationDto?> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var annotation = await db.SupplierReviewAnnotations
            .Where(a => a.SupplierId == scope.SupplierId && a.ResolvedAt == null)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (annotation is null) return null;

        var typesById = await db.DocumentTypes.ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        return new ReviewAnnotationDto(
            annotation.Id, annotation.RequestedAt, annotation.Reason, annotation.FlaggedProfileFields,
            [.. annotation.FlaggedDocumentTypeIds.Select(id => typesById.GetValueOrDefault(id, id.ToString()))],
            annotation.ResolvedAt);
    }
}

file static class ReviewerNotify
{
    /// <summary>The supplier's primary user, as an id rather than an address (MSP-89). The address
    /// is resolved inside the job so it never reaches the Hangfire store.</summary>
    public static async Task<Guid?> GetPrimaryUserIdAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        await db.Users.Where(u => u.SupplierId == supplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);

    /// <summary>BUSINESS-PROCESSES.md: InfoRequested -> Resubmitted notifies "reviewer" - there is
    /// no per-application reviewer assignment (pickup doesn't record who), so this notifies the
    /// whole onboarding_reviewer pool, matching how Submit already "queue[s] to onboarding review
    /// pool" rather than a named individual.</summary>
    public static async Task<IReadOnlyList<Guid>> GetReviewerPoolUserIdsAsync(AppDbContext db, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               join u in db.Users on ur.UserId equals u.Id
               where r.Name == Roles.OnboardingReviewer
               select u.Id)
            .Distinct()
            .ToListAsync(ct);
}

public sealed class PickUpApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IPickUpApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.PickUpForReview(); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_picked_up", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

public sealed class ApproveApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IApproveApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        var blocking = await DocumentCompletenessEvaluator.GetBlockingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);

        try { supplier.Approve(blocking); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        // FEAT-03.5: Outbox event written in the SAME SaveChangesAsync transaction as the state
        // change - approval never blocks on ERP being up, and the event is guaranteed atomic
        // with the approval (docs/architecture/DOMAIN-MODEL.md §5.3).
        var payload = JsonSerializer.Serialize(new
        {
            supplierId = supplier.Id,
            referenceCode = supplier.ReferenceCode,
            displayNameAr = supplier.DisplayNameAr,
            displayNameEn = supplier.DisplayNameEn,
            approvedAt = DateTimeOffset.UtcNow,
        });
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "SupplierApproved",
            PayloadJson = payload,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_approved", scope.UserId, toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendApplicationApprovedEmailAsync(userId.Value, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

public sealed class RejectApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IRejectApplicationHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(string referenceCode, string reason, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.Reject(reason); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_rejected", scope.UserId, toState: supplier.OnboardingState.ToString(), reason: reason, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendApplicationRejectedEmailAsync(userId.Value, reason, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

public sealed class RequestInfoHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IRequestInfoHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(RequestInfoCommand command, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == command.ReferenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.RequestInfo(); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        var flaggedTypeIds = await db.DocumentTypes
            .Where(t => command.FlaggedDocumentTypeCodes.Contains(t.Code))
            .Select(t => t.Id)
            .ToListAsync(ct);

        // Held in a local so the enqueue below can reference the annotation by id. The reason text
        // is persisted here, so the job resolves it rather than carrying it (MSP-89).
        var annotation = new SupplierReviewAnnotation
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            RequestedByUserId = scope.UserId ?? Guid.Empty,
            RequestedAt = DateTimeOffset.UtcNow,
            Reason = command.Reason,
            FlaggedProfileFields = [.. command.FlaggedProfileFields],
            FlaggedDocumentTypeIds = [.. flaggedTypeIds],
        };

        db.SupplierReviewAnnotations.Add(annotation);

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_info_requested", scope.UserId, toState: supplier.OnboardingState.ToString(), reason: command.Reason, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendInfoRequestedEmailAsync(userId.Value, annotation.Id, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

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

        try
        {
            supplier.Resubmit();
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

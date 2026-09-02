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

public sealed class ListReviewQueueHandler(AppDbContext db, IScopeContext scope) : IListReviewQueueHandler
{
    /// <summary>Audit actions that mark an application (re)entering the reviewer's active queue -
    /// see ReviewQueueItemDto.EnteredQueueAt's own doc comment for why this isn't CreatedAt.</summary>
    private static readonly string[] ReviewQueueEntryActions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    /// <summary>
    /// Keyed by the same vocabulary the endpoint validates against
    /// (<see cref="ReviewQueueFilterValues.States"/>), and case-SENSITIVE to match it - a map that
    /// accepted "underreview" while the endpoint's allow-list did not would put the two out of step
    /// in the direction that silently widens.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SupplierOnboardingState> StateFilterMap = new Dictionary<string, SupplierOnboardingState>(StringComparer.Ordinal)
    {
        ["Submitted"] = SupplierOnboardingState.Submitted,
        ["UnderReview"] = SupplierOnboardingState.UnderReview,
        ["InfoRequested"] = SupplierOnboardingState.InfoRequested,
    };

    public async Task<ListEnvelope<ReviewQueueItemDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? assignedTo, CancellationToken ct)
    {
        var states = state is not null && StateFilterMap.TryGetValue(state, out var single)
            ? [single]
            : new[] { SupplierOnboardingState.Submitted, SupplierOnboardingState.UnderReview, SupplierOnboardingState.InfoRequested };
        var pageSize = ListEnvelope<ReviewQueueItemDto>.ClampPageSize(limit);

        var query = db.Suppliers.Where(s => states.Contains(s.OnboardingState));

        // FEAT-03.6: "me" resolves against the caller so the frontend never needs to know its own
        // user id; "unassigned" surfaces the pool a reviewer would actually claim from; anything
        // else is treated as a literal reviewer user id (a manager filtering by a specific
        // reviewer).
        if (assignedTo == "me")
        {
            query = query.Where(s => s.AssignedReviewerId == scope.UserId);
        }
        else if (assignedTo == "unassigned")
        {
            query = query.Where(s => s.AssignedReviewerId == null);
        }
        else if (assignedTo is not null && Guid.TryParse(assignedTo, out var reviewerId))
        {
            query = query.Where(s => s.AssignedReviewerId == reviewerId);
        }

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (ReviewQueueCursor.TryDecode(cursor, out var from))
        {
            // Strictly "after" the cursor row in ascending order (oldest submission first, the
            // order a reviewer should work the queue). The Id tie-break is what keeps this safe
            // when two suppliers register in the same tick.
            query = query.Where(s =>
                s.CreatedAt > from.CreatedAt
                || (s.CreatedAt == from.CreatedAt && s.Id.CompareTo(from.Id) > 0));
        }

        // limit + 1: the extra row answers HasMore without a COUNT over a queue new applications
        // are inserted into continuously.
        var rows = await query
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.CreatedAt, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, OnboardingState = s.OnboardingState.ToString(), s.AssignedReviewerId })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        // FEAT-03.6: most recent "(re)entered the active queue" audit row per supplier on this
        // page - a second, small query rather than a join on the paged query above, since only
        // the page's own rows (at most pageSize) need it.
        var pageIds = items.Select(r => r.Id).ToList();
        var enteredQueueAtBySupplier = await db.AuditLogs
            .Where(a => a.AggregateType == "Supplier" && pageIds.Contains(a.AggregateId) && ReviewQueueEntryActions.Contains(a.Action))
            .GroupBy(a => a.AggregateId)
            .Select(g => new { SupplierId = g.Key, EnteredAt = g.Max(a => a.OccurredAt) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.EnteredAt, ct);

        var reviewerIds = items.Where(r => r.AssignedReviewerId is not null).Select(r => r.AssignedReviewerId!.Value).Distinct().ToList();
        var reviewerNamesById = await db.Users
            .Where(u => reviewerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        var dtos = items
            .Select(r => new ReviewQueueItemDto(
                r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn, r.OnboardingState,
                enteredQueueAtBySupplier.GetValueOrDefault(r.Id, r.CreatedAt),
                r.AssignedReviewerId,
                r.AssignedReviewerId is { } rid ? reviewerNamesById.GetValueOrDefault(rid) : null))
            .ToList();

        return ListEnvelope<ReviewQueueItemDto>.Cursor(
            dtos,
            hasMore,
            hasMore ? new ReviewQueueCursor(items[^1].CreatedAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "createdAt",
            filtersApplied: DescribeFilters(state, assignedTo));
    }
    /// <summary>
    /// The filters actually applied, for the envelope's <c>meta.filtersApplied</c> (§5.2, whose
    /// example renders them as <c>["state=UnderReview,Rejected"]</c>). Null when unfiltered, so a
    /// caller looking at an empty queue can tell "nothing is queued" from "nothing matched".
    /// </summary>
    private static IReadOnlyList<string>? DescribeFilters(string? state, string? assignedTo)
    {
        List<string> applied = [];
        if (state is not null) applied.Add($"state={state}");
        if (assignedTo is not null) applied.Add($"assignedTo={assignedTo}");
        return applied.Count == 0 ? null : applied;
    }
}

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

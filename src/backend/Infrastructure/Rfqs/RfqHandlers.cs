using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

internal static class RfqDtoMapper
{
    /// <summary>Async because InvitationDto carries the invited supplier's display names
    /// (FEAT-08.7) - a small extra query per call, acceptable at this call volume (buyer-side RFQ
    /// mutation/detail endpoints, not a hot list path); ListRfqsHandler batches it once for the
    /// whole page instead of N+1 (see its own comment).</summary>
    public static async Task<RfqDto> ToDtoAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        var supplierIds = rfq.Invitations.Select(i => i.SupplierId)
            .Concat(rfq.Clarifications.Select(c => c.AskedBySupplierId)).Distinct().ToList();
        var names = await SupplierNamesAsync(db, supplierIds, ct);
        return ToDto(rfq, names);
    }

    public static async Task<Dictionary<Guid, (string Ar, string En)>> SupplierNamesAsync(
        AppDbContext db, IReadOnlyList<Guid> supplierIds, CancellationToken ct)
    {
        if (supplierIds.Count == 0) return [];
        return await db.Suppliers.Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, s => (s.DisplayNameAr, s.DisplayNameEn), ct);
    }

    public static RfqDto ToDto(Rfq rfq, IReadOnlyDictionary<Guid, (string Ar, string En)> supplierNames) => new(
        rfq.ReferenceCode, rfq.OrganizationId, rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn,
        rfq.CurrencyCode, rfq.State, rfq.PublishAt, rfq.SubmissionOpensAt, rfq.SubmissionClosesAt,
        rfq.ClarificationDeadlineAt, rfq.EvaluationTargetDate, rfq.EvaluationTemplateId, rfq.EvaluationTemplateVersion,
        rfq.CancelReason,
        [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
            i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn, i.CategoryCode,
            i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
        [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode))],
        [.. rfq.Attachments.Select(a => new RfqAttachmentDto(a.Id, a.OriginalFileName, a.ContentType, a.Caption, a.UploadedAt))],
        [.. rfq.Approvals.OrderBy(a => a.StepNo).Select(a => new RfqApprovalDto(a.StepNo, a.ApproverUserId, a.Decision, a.Comment, a.DecidedAt))],
        [.. rfq.Invitations.OrderBy(i => i.InvitedAt).Select(i =>
        {
            (string Ar, string En) name = supplierNames.TryGetValue(i.SupplierId, out var n) ? n : ("", "");
            return new InvitationDto(i.Id, i.SupplierId, name.Ar, name.En, i.Status, i.InvitedAt, i.ViewedAt, i.RespondedAt, i.DeclineReason);
        })],
        [.. rfq.Clarifications.OrderBy(c => c.AskedAt).Select(c =>
        {
            (string Ar, string En) name = supplierNames.TryGetValue(c.AskedBySupplierId, out var n) ? n : ("", "");
            return new ClarificationDto(c.Id, c.AskedBySupplierId, name.Ar, name.En, c.Question, c.Answer, c.Visibility, c.AskedAt, c.AnsweredAt);
        })],
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))]);

    /// <summary>FEAT-10.3/FR-CLR-003: the anonymization boundary. Only <paramref name="supplierId"/>'s
    /// own clarifications (any Visibility) plus every OTHER supplier's PublishedToAll clarifications
    /// are included - a PrivateToAsker item belonging to someone else is not just anonymized, it is
    /// entirely absent from this list, matching OQ-008's "private to the asking supplier". IsMine is
    /// computed here, server-side, from the real AskedBySupplierId - never trust a client-supplied
    /// flag for this.</summary>
    public static SupplierRfqDto ToSupplierDto(Rfq rfq, Invitation myInvitation, Guid supplierId) => new(
        rfq.ReferenceCode, rfq.TitleAr, rfq.TitleEn, rfq.DescriptionAr, rfq.DescriptionEn, rfq.CurrencyCode, rfq.State,
        rfq.SubmissionOpensAt, rfq.SubmissionClosesAt, rfq.ClarificationDeadlineAt,
        [.. rfq.Items.OrderBy(i => i.LineNo).Select(i => new RfqItemDto(
            i.Id, i.LineNo, i.TitleAr, i.TitleEn, i.SpecificationAr, i.SpecificationEn, i.CategoryCode,
            i.Quantity, i.UnitOfMeasureCode, i.IsUnitPrice, i.IsOptional))],
        [.. rfq.Requirements.Select(r => new RequirementDto(r.Id, r.TextAr, r.TextEn, r.IsMandatory, r.DocumentTypeCode))],
        [.. rfq.Attachments.Select(a => new RfqAttachmentDto(a.Id, a.OriginalFileName, a.ContentType, a.Caption, a.UploadedAt))],
        myInvitation.Status,
        [.. rfq.Clarifications
            .Where(c => c.AskedBySupplierId == supplierId || c.Visibility == ClarificationVisibility.PublishedToAll)
            .OrderBy(c => c.AskedAt)
            .Select(c => new SupplierClarificationDto(c.Id, c.Question, c.Answer, c.Visibility, c.AskedAt, c.AnsweredAt, c.AskedBySupplierId == supplierId))],
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))]);
}

/// <summary>Shared loader: every RFQ handler in this file row-scopes to the caller's own
/// OrganizationId (BRULE-029: "An RFQ is created and owned by a procurement_officer and is scoped
/// to their Organization; cross-org authoring is prohibited"). A null scope.OrganizationId (e.g. a
/// supplier-side or platform caller with no org membership) can never see or touch any RFQ - same
/// "no scope, no access" pattern as scope.SupplierId is null on the supplier side.</summary>
file static class RfqLoader
{
    // AsSplitQuery: four sibling collections in one single JOIN query produces a cartesian-product
    // row multiplication (Items x Requirements x Attachments x Approvals) - not itself the cause
    // of a real concurrency bug found while building this (see SubmitRfqForReviewHandler's own
    // comment for that one), but a real, separate performance concern worth avoiding regardless
    // once four sibling collections are all included together.
    public static IQueryable<Rfq> IncludeAll(this DbSet<Rfq> set) =>
        set.Include(r => r.Items).Include(r => r.Requirements).Include(r => r.Attachments).Include(r => r.Approvals)
            .Include(r => r.Invitations).Include(r => r.Clarifications).Include(r => r.Addenda)
            .AsSplitQuery();

    public static async Task<Rfq?> LoadScopedAsync(AppDbContext db, IScopeContext scope, string referenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        return await db.Rfqs.IncludeAll()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode && r.OrganizationId == scope.OrganizationId, ct);
    }
}

public sealed class ListRfqsHandler(AppDbContext db, IScopeContext scope) : IListRfqsHandler
{
    public async Task<IReadOnlyList<RfqDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.OrganizationId is null) return [];
        var rfqs = await db.Rfqs.IncludeAll()
            .Where(r => r.OrganizationId == scope.OrganizationId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        // Batched once for the whole page rather than N+1 (ToDtoAsync's own per-call query would
        // otherwise fire once per RFQ in the list).
        var supplierIds = rfqs.SelectMany(r => r.Invitations).Select(i => i.SupplierId).Distinct().ToList();
        var names = await RfqDtoMapper.SupplierNamesAsync(db, supplierIds, ct);
        return [.. rfqs.Select(r => RfqDtoMapper.ToDto(r, names))];
    }
}

public sealed class GetRfqHandler(AppDbContext db, IScopeContext scope) : IGetRfqHandler
{
    public async Task<RfqDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, referenceCode, ct);
        return rfq is null ? null : await RfqDtoMapper.ToDtoAsync(db, rfq, ct);
    }
}

/// <summary>FEAT-07.1/FR-RFQ-001/BRULE-029.</summary>
public sealed class CreateRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICreateRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(CreateRfqCommand command, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "RFQ", ct);

        Rfq rfq;
        try
        {
            rfq = Rfq.Create(
                referenceCode, scope.OrganizationId.Value, command.TitleAr, command.TitleEn,
                command.DescriptionAr, command.DescriptionEn, command.CurrencyCode,
                command.PublishAt, command.SubmissionOpensAt, command.SubmissionClosesAt,
                command.ClarificationDeadlineAt, command.EvaluationTargetDate);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.Rfqs.Add(rfq);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_created", scope.UserId, referenceCode: rfq.ReferenceCode, toState: nameof(RfqState.Draft), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

public sealed class UpdateRfqBasicsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUpdateRfqBasicsHandler
{
    public async Task<RfqMutationResult> HandleAsync(UpdateRfqBasicsCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.UpdateBasics(
                command.TitleAr, command.TitleEn, command.DescriptionAr, command.DescriptionEn, command.CurrencyCode,
                command.PublishAt, command.SubmissionOpensAt, command.SubmissionClosesAt,
                command.ClarificationDeadlineAt, command.EvaluationTargetDate);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_updated", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.1/FR-RFQ-002. Category/UoM referential integrity validated against reference
/// data by code, not a DB FK - same established convention as Offering (see
/// OfferingContracts.cs's own doc comment).</summary>
public sealed class ManageRfqItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRfqItemHandler
{
    public async Task<RfqMutationResult> AddAsync(AddRfqItemCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        if (!await db.Categories.AnyAsync(c => c.Code == command.CategoryCode, ct))
        {
            return new RfqMutationResult.InvalidCategory();
        }
        if (!await db.UnitsOfMeasure.AnyAsync(u => u.Code == command.UnitOfMeasureCode, ct))
        {
            return new RfqMutationResult.InvalidUnitOfMeasure();
        }

        RfqItem item;
        try
        {
            item = rfq.AddItem(
                command.TitleAr, command.TitleEn, command.SpecificationAr, command.SpecificationEn,
                command.CategoryCode, command.Quantity, command.UnitOfMeasureCode, command.IsUnitPrice, command.IsOptional);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.RfqItems.Add(item);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_item_added", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    public async Task<RfqMutationResult> RemoveAsync(RemoveRfqItemCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RemoveItem(command.ItemId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_item_removed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

public sealed class ManageRequirementHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRequirementHandler
{
    public async Task<RfqMutationResult> AddAsync(AddRequirementCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Requirement requirement;
        try
        {
            requirement = rfq.AddRequirement(command.TextAr, command.TextEn, command.IsMandatory, command.DocumentTypeCode);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.Requirements.Add(requirement);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_requirement_added", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    public async Task<RfqMutationResult> RemoveAsync(RemoveRequirementCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RemoveRequirement(command.RequirementId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_requirement_removed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.2/FR-RFQ-003. The caller (endpoint) has already stored the file via
/// IFileStorage before this runs - same split as UploadDocumentHandler.</summary>
public sealed class ManageRfqAttachmentHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRfqAttachmentHandler
{
    public async Task<RfqMutationResult> AddAsync(AddRfqAttachmentCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        RfqAttachment attachment;
        try
        {
            attachment = rfq.AddAttachment(command.StorageKey, command.OriginalFileName, command.ContentType, command.Caption);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.RfqAttachments.Add(attachment);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_attachment_added", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    public async Task<RfqMutationResult> RemoveAsync(RemoveRfqAttachmentCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RemoveAttachment(command.AttachmentId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_attachment_removed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.3/FR-RFQ-004: binds a version-snapshotted EvaluationTemplateRef. Loads the
/// live EvaluationTemplate (must be Active), serializes its current criteria as the frozen
/// snapshot, marks it IsReferenced (immutable from here on unless forked - EvaluationTemplate.cs's
/// own doc comment), and binds the RFQ to that exact Id+Version. Both aggregates are saved in the
/// same SaveChangesAsync call - a pragmatic single-unit-of-work exception to "one aggregate per
/// transaction" (DOMAIN-MODEL.md §8), justified the same way AuditLogger already is: marking a
/// template referenced is not a domain event that needs eventual consistency, it is the direct,
/// synchronous consequence of the bind command the caller just issued.</summary>
public sealed class BindEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IBindEvaluationTemplateHandler
{
    public async Task<RfqMutationResult> HandleAsync(BindEvaluationTemplateCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var template = await db.EvaluationTemplates.Include(t => t.Criteria)
            .FirstOrDefaultAsync(t => t.Id == command.EvaluationTemplateId, ct);
        if (template is null)
        {
            return new RfqMutationResult.InvalidEvaluationTemplate("Evaluation template not found.");
        }
        if (template.Status != EvaluationTemplateStatus.Active)
        {
            return new RfqMutationResult.InvalidEvaluationTemplate("Only an Active evaluation template can be bound to an RFQ.");
        }

        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.Id,
            c.NameAr,
            c.NameEn,
            Dimension = c.Dimension.ToString(),
            c.Weight,
            c.MaxScore,
            c.Threshold,
            ScoringType = c.ScoringType.ToString(),
        }));

        try
        {
            rfq.BindEvaluationTemplate(template.Id, template.Version, snapshotJson);
            template.MarkReferenced();
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_evaluation_template_bound", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: $"{template.Id}/v{template.Version}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: Draft -&gt; InternalReview.</summary>
public sealed class SubmitRfqForReviewHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitRfqForReviewHandler
{
    public async Task<RfqMutationResult> HandleAsync(SubmitRfqForReviewCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.SubmitForReview();
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        // Same client-assigned-GUIDv7 gotcha as every other child Add in this codebase
        // (ManageContactHandler.cs's own comment): without this, EF's graph-tracking heuristic
        // sees a non-default Id on the new RfqApproval and marks it Modified instead of Added,
        // issuing an UPDATE against a row that does not exist yet - 0 rows affected -
        // DbUpdateConcurrencyException on the NEXT SaveChanges that touches this aggregate.
        db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submitted_for_review", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Draft), toState: nameof(RfqState.InternalReview), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -&gt; Draft, "return for
/// edits".</summary>
public sealed class ReturnRfqForEditsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReturnRfqForEditsHandler
{
    public async Task<RfqMutationResult> HandleAsync(ReturnRfqForEditsCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.ReturnForEdits(scope.UserId.Value, command.Comments);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_returned", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.InternalReview), toState: nameof(RfqState.Draft), reason: command.Comments, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -&gt; Approved. OQ-004 interim
/// single-approver - see RfqApproval.cs's own doc comment for why the schema is an array anyway.</summary>
public sealed class ApproveRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IApproveRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(ApproveRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.Approve(scope.UserId.Value);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_approved", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.InternalReview), toState: nameof(RfqState.Approved), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.5/BUSINESS-PROCESSES.md §3.1: Approved -&gt; Published. BRULE-032/EPIC-08 gap
/// closed: re-checks every currently-invited supplier's LifecycleState before publishing, since
/// InviteSupplierHandler only guarantees Active AT INVITE time - time may have passed (and a
/// supplier may have been suspended/deactivated) between invite and this transition.
/// FEAT-13.3 audit gap fix: notifies every invited supplier that submissions are now open -
/// previously only the invite-time email told them an RFQ existed at all, with nothing marking the
/// actual open-for-submission moment.</summary>
public sealed class PublishRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IPublishRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(PublishRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        if (invitedSupplierIds.Count > 0)
        {
            var inactiveCount = await db.Suppliers
                .Where(s => invitedSupplierIds.Contains(s.Id) && s.LifecycleState != SupplierLifecycleState.Active)
                .CountAsync(ct);
            if (inactiveCount > 0)
            {
                return new RfqMutationResult.SupplierNotActive();
            }
        }

        try
        {
            rfq.Publish();
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_published", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.Approved), toState: nameof(RfqState.Published), ct: ct);
        await db.SaveChangesAsync(ct);

        if (invitedSupplierIds.Count > 0)
        {
            var userIds = await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value))
                .Select(u => u.Id).ToListAsync(ct);
            foreach (var userId in userIds)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqPublishedEmailAsync(userId, rfq.Id, CancellationToken.None));
            }
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.6/BUSINESS-PROCESSES.md §3.1: manual early close of the submission window
/// (the scheduled deadline-driven close is RfqTimelineJob, a system actor, not this handler).</summary>
public sealed class CloseRfqSubmissionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICloseRfqSubmissionHandler
{
    public async Task<RfqMutationResult> HandleAsync(CloseRfqSubmissionCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.CloseSubmissionWindow(command.Reason, isEarlyClose: true);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_closed", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.SubmissionOpen), toState: nameof(RfqState.SubmissionClosed), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-07.8/BUSINESS-PROCESSES.md §3.1: cancel from any pre-Awarded state, reason
/// mandatory. FEAT-13.3 audit gap fix: notifies every invited supplier AND, if an Evaluation had
/// already been opened, every assigned evaluator - both had work in flight on this RFQ that just
/// became moot, and neither was told before this fix.</summary>
public sealed class CancelRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : ICancelRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(CancelRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var fromState = rfq.State;
        try
        {
            rfq.Cancel(command.Reason);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_cancelled", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(RfqState.Cancelled), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        if (invitedSupplierIds.Count > 0)
        {
            var supplierUserIds = await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value))
                .Select(u => u.Id).ToListAsync(ct);
            foreach (var userId in supplierUserIds)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqCancelledEmailAsync(userId, rfq.Id, CancellationToken.None));
            }
        }

        var evaluationId = await db.Evaluations.Where(e => e.RfqId == rfq.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var evaluatorUserIds = evaluationId is null
            ? []
            : await db.EvaluationAssignments
                .Where(a => a.EvaluationId == evaluationId.Value && a.RecusedAt == null)
                .Select(a => a.EvaluatorUserId).Distinct().ToListAsync(ct);
        foreach (var userId in evaluatorUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqCancelledEmailAsync(userId, rfq.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-08.1/FR-INV-001/BRULE-032: invite a candidate supplier. Active-only is enforced
/// here (cross-aggregate - see Rfq.InviteSupplier's own doc comment), not on the domain method.
/// FEAT-08.3/FR-INV-003: on success, enqueues a real email (not Outbox - see EmailJobs.cs's own
/// doc comment on why token/notification emails use the Hangfire+IEmailSender path, not the
/// ERP-integration Outbox) to the invited supplier's primary user. "In-app" is the invited
/// supplier's own RFQ list reflecting the new invitation on next fetch - the same shape "in-app"
/// has in every other transition in this codebase (no dedicated Notification entity exists
/// anywhere yet; EPIC-15 is unbuilt), not a gap invented for this feature alone.</summary>
public sealed class InviteSupplierHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IInviteSupplierHandler
{
    public async Task<RfqMutationResult> HandleAsync(InviteSupplierCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == command.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new RfqMutationResult.SupplierNotActive();
        }

        Invitation invitation;
        try
        {
            invitation = rfq.InviteSupplier(command.SupplierId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.Invitations.Add(invitation);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_supplier_invited", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"supplierId\":\"{command.SupplierId}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var recipientUserId = await db.Users.Where(u => u.SupplierId == command.SupplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (recipientUserId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqInvitationEmailAsync(recipientUserId.Value, rfq.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-08.2/FR-INV-002: suggests suppliers whose Offerings match one of the RFQ's item
/// categories, Active-only, already-invited suppliers excluded, ranked by match count. Reuses the
/// same manual Offering-to-Supplier join as SearchBuyerOfferingsHandler (OfferingHandlers.cs's own
/// comment: Offering and Supplier are separate aggregate roots with no EF navigation between
/// them) - a suggestion, not a binding action: the officer still calls InviteSupplier per
/// candidate.</summary>
public sealed class SuggestInvitationCandidatesHandler(AppDbContext db, IScopeContext scope) : ISuggestInvitationCandidatesHandler
{
    public async Task<IReadOnlyList<InvitationCandidateDto>> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, referenceCode, ct);
        if (rfq is null) return [];

        var categoryCodes = rfq.Items.Select(i => i.CategoryCode).Distinct().ToList();
        var alreadyInvited = rfq.Invitations.Select(i => i.SupplierId).ToHashSet();
        if (categoryCodes.Count == 0) return [];

        var matches = await (
            from o in db.Offerings
            where o.IsActive && categoryCodes.Contains(o.CategoryCode)
            select new { o.SupplierId, o.CategoryCode })
            .Distinct()
            .ToListAsync(ct);

        var candidateIds = matches.Select(m => m.SupplierId).Distinct().Where(id => !alreadyInvited.Contains(id)).ToList();
        if (candidateIds.Count == 0) return [];

        var activeSuppliers = await db.Suppliers
            .Where(s => candidateIds.Contains(s.Id) && s.LifecycleState == SupplierLifecycleState.Active)
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToListAsync(ct);

        var matchCounts = matches.Where(m => activeSuppliers.Select(s => s.Id).Contains(m.SupplierId))
            .GroupBy(m => m.SupplierId).ToDictionary(g => g.Key, g => g.Select(m => m.CategoryCode).Distinct().Count());

        return [.. activeSuppliers
            .Select(s => new InvitationCandidateDto(s.Id, s.DisplayNameAr, s.DisplayNameEn, matchCounts.GetValueOrDefault(s.Id, 0)))
            .OrderByDescending(c => c.MatchCount)];
    }
}

/// <summary>FEAT-10.2/FR-CLR-002, OQ-008 interim (private-by-default, explicit publish available):
/// command.Publish defaults to false at the API layer. Notifies only the asker - a
/// PublishedToAll answer additionally notifies every other invited supplier via
/// PublishClarificationHandler below (answering-and-publishing-at-once still only reaches the
/// asker at answer time here; the visibility flip's own notification covers the rest, kept as one
/// notification per actual visibility change rather than double-emailing on the combined
/// action).</summary>
public sealed class AnswerClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IAnswerClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(AnswerClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Clarification clarification;
        try
        {
            rfq.AnswerClarification(command.ClarificationId, command.Answer, command.Publish);
            clarification = rfq.Clarifications.Single(c => c.Id == command.ClarificationId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_answered", scope.UserId, referenceCode: rfq.ReferenceCode,
            changes: $"{{\"clarificationId\":\"{command.ClarificationId}\",\"published\":{(command.Publish ? "true" : "false")}}}", ct: ct);
        await db.SaveChangesAsync(ct);

        await NotifyAskerAsync(db, backgroundJobs, clarification.AskedBySupplierId, rfq.Id, clarification.Id, ct);
        if (command.Publish)
        {
            await NotifyOtherInviteesAsync(db, backgroundJobs, rfq, clarification.AskedBySupplierId, ct);
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    internal static async Task NotifyAskerAsync(AppDbContext db, IBackgroundJobClient backgroundJobs, Guid supplierId, Guid rfqId, Guid clarificationId, CancellationToken ct)
    {
        var userId = await db.Users.Where(u => u.SupplierId == supplierId).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationAnsweredEmailAsync(userId.Value, rfqId, clarificationId, CancellationToken.None));
        }
    }

    internal static async Task NotifyOtherInviteesAsync(AppDbContext db, IBackgroundJobClient backgroundJobs, Rfq rfq, Guid excludeSupplierId, CancellationToken ct)
    {
        var otherSupplierIds = rfq.Invitations.Select(i => i.SupplierId).Where(id => id != excludeSupplierId).ToList();
        if (otherSupplierIds.Count == 0) return;

        var userIds = await db.Users.Where(u => u.SupplierId != null && otherSupplierIds.Contains(u.SupplierId.Value))
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationPublishedEmailAsync(userId, rfq.Id, CancellationToken.None));
        }
    }
}

/// <summary>FEAT-10.2/FR-CLR-002: promotes a privately-answered clarification to PublishedToAll -
/// notifies every OTHER invited supplier (the asker already knows their own answer).</summary>
public sealed class PublishClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IPublishClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(PublishClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Clarification clarification;
        try
        {
            rfq.PublishClarification(command.ClarificationId);
            clarification = rfq.Clarifications.Single(c => c.Id == command.ClarificationId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_published", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"clarificationId\":\"{command.ClarificationId}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        await AnswerClarificationHandler.NotifyOtherInviteesAsync(db, backgroundJobs, rfq, clarification.AskedBySupplierId, ct);

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>FEAT-10.4/FR-CLR-004/FR-RFQ-012: the first real use of "locked after Published except
/// addenda" (Rfq.IssueAddendum's own doc comment). Notifies every invited supplier.</summary>
public sealed class IssueAddendumHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IIssueAddendumHandler
{
    public async Task<RfqMutationResult> HandleAsync(IssueAddendumCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Addendum addendum;
        try
        {
            addendum = rfq.IssueAddendum(command.TitleAr, command.TitleEn, command.DescriptionAr, command.DescriptionEn, scope.UserId.Value);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.Addenda.Add(addendum);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_addendum_issued", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"addendumId\":\"{addendum.Id}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        var userIds = invitedSupplierIds.Count == 0
            ? []
            : await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value)).Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqAddendumEmailAsync(userId, rfq.Id, addendum.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
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
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))],
        rfq.RowVersion,
        rfq.SubmissionDeadlineChangeReason, rfq.SubmissionDeadlineChangedAt);

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
        [.. rfq.Addenda.OrderBy(a => a.IssuedAt).Select(a => new AddendumDto(a.Id, a.TitleAr, a.TitleEn, a.DescriptionAr, a.DescriptionEn, a.IssuedAt))],
        rfq.SubmissionDeadlineChangeReason, rfq.SubmissionDeadlineChangedAt);
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

/// <summary>
/// T2 Item 2 + 3: projected and cursor-paginated.
///
/// <para>This previously ran <c>IncludeAll()</c> - seven child collections - plus a batched
/// supplier-name query, for every RFQ in the organization, unpaginated, to render three scalar
/// columns. The includes existed for <see cref="RfqDto"/>, the DETAIL shape; the list never touched
/// them. It now projects <see cref="RfqListItemDto"/> in SQL and pages by keyset per
/// API-ARCHITECTURE.md §6.1, which names RFQs a cursor-default collection.</para>
///
/// <para>Org scoping is unchanged and still the first predicate applied - it is part of the same
/// WHERE the cursor narrows, so it holds on page two exactly as on page one.</para>
/// </summary>
public sealed class ListRfqsHandler(AppDbContext db, IScopeContext scope) : IListRfqsHandler
{
    public async Task<ListEnvelope<RfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct)
    {
        var size = ListEnvelope<RfqListItemDto>.ClampPageSize(pageSize);
        if (scope.OrganizationId is null) return ListEnvelope<RfqListItemDto>.Empty(size);

        var query = db.Rfqs.Where(r => r.OrganizationId == scope.OrganizationId);

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (RfqListCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(r =>
                r.CreatedAt < from.CreatedAt
                || (r.CreatedAt == from.CreatedAt && r.Id.CompareTo(from.Id) < 0));
        }

        // pageSize + 1: the extra row answers HasMore without a COUNT over the whole filtered set.
        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, Dto = new RfqListItemDto(r.ReferenceCode, r.TitleAr, r.TitleEn, r.State, r.CreatedAt) })
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        var items = hasMore ? rows[..size] : rows;

        return ListEnvelope<RfqListItemDto>.Cursor(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new RfqListCursor(items[^1].Dto.CreatedAt, items[^1].Id).Encode() : null,
            size,
            totalCount,
            sort: "-createdAt");
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
        // No IllegalTransition branch here: creation has no current state to report an allowed-next
        // set against, so every refusal from Rfq.Create is a 400 about the request.
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

/// <summary>
/// T-018/BRULE-035: <i>"Deadline extension while Published/SubmissionOpen: procurement_officer may
/// extend submissionCloseAt (audit rfq.deadline_extended, notify all invitees). Shortening the window
/// requires procurement_manager."</i>
/// </summary>
public sealed class ChangeSubmissionDeadlineHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IChangeSubmissionDeadlineHandler
{
    public async Task<RfqMutationResult> HandleAsync(ChangeSubmissionDeadlineCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        // BOTH permission checks live here, not on the route. The direction decides which one applies
        // and the direction is only knowable once the current deadline has been read - and the two
        // permissions belong to two different roles, so a single route filter would lock out whichever
        // caller it did not name. Checked BEFORE the mutation, so a refusal leaves the aggregate
        // untouched.
        var wouldShorten = rfq.SubmissionClosesAt is { } current && command.NewCloseAt < current;
        var required = wouldShorten ? Permissions.RfqDeadlineShorten : Permissions.RfqEdit;
        if (!scope.HasPermission(required))
        {
            return new RfqMutationResult.DeadlineChangeNotPermitted();
        }

        var previous = rfq.SubmissionClosesAt;
        bool shortened;
        try
        {
            shortened = rfq.ChangeSubmissionDeadline(command.NewCloseAt, command.Reason);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        // BRULE-035 names rfq.deadline_extended for the extension. The shortening gets its own action
        // rather than sharing it: an audit search for "who cut this tender short" must not have to
        // read two timestamps out of a row named "extended".
        //
        // D-12: there is no cap on an extension, so THIS ROW is the control. Both dates are recorded,
        // because "extended" without the from/to says nothing about by how much.
        // A-6: the reason joins the row. D-12 left the extension uncapped and called the audit row the
        // control; without a reason that row records only that someone moved a date, which is not a
        // control anyone can act on.
        await auditLogger.LogAsync("Rfq", rfq.Id,
            shortened ? "rfq.deadline_shortened" : "rfq.deadline_extended",
            scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: previous?.ToString("O"), toState: command.NewCloseAt.ToString("O"),
            reason: command.Reason, ct: ct);

        // "notify all invitees" - every invited supplier's users, not the committee. A deadline change
        // is only news to the people bidding against it.
        NotificationOutbox.EnqueueMany(db,
            shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended,
            await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
            // The new date is in the dedupe key: two successive changes are two pieces of news, and a
            // key on the RFQ alone would silently swallow the second.
            $"{(shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended)}:{rfq.Id}:{command.NewCloseAt:O}",
            // No date in the payload. BRULE-091's allow-list treats a date as content, and the copy
            // points at the RFQ instead - see NotificationPayload.AllowedKeys.
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode });

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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            // T-021: carried into the snapshot so the rule is the one the RFQ bound, not the one the
            // template happens to hold now.
            c.RequiresJustification,
        }));

        try
        {
            rfq.BindEvaluationTemplate(template.Id, template.Version, snapshotJson);
            template.MarkReferenced();
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex, RfqState.InternalReview);
        }

        // Same client-assigned-GUIDv7 gotcha as every other child Add in this codebase
        // (ManageContactHandler.cs's own comment): without this, EF's graph-tracking heuristic
        // sees a non-default Id on the new RfqApproval and marks it Modified instead of Added,
        // issuing an UPDATE against a row that does not exist yet - 0 rows affected -
        // DbUpdateConcurrencyException on the NEXT SaveChanges that touches this aggregate.
        db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));

        // §3.1 "Draft -> InternalReview | In-app to `procurement_manager`". Enqueued INSIDE the
        // transaction (D-5): a notification must not fire for a submission that rolled back.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqSubmittedForReview,
            await NotificationRecipients.ProcurementManagersAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.RfqSubmittedForReview}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

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
            return RfqTransitions.Refusal(rfq, ex, RfqState.Draft);
        }

        // §3.1 "InternalReview -> Draft | In-app to officer". The OFFICER POOL, not the individual:
        // nothing records which officer owns an RFQ. Reported as an open question.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqReturnedForEdits,
            await NotificationRecipients.ProcurementOfficersAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.RfqReturnedForEdits}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });
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
            return RfqTransitions.Refusal(rfq, ex, RfqState.Approved);
        }

        // §3.1 "InternalReview -> Approved | In-app to officer".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqApproved,
            await NotificationRecipients.ProcurementOfficersAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.RfqApproved}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });
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
            return RfqTransitions.Refusal(rfq, ex, RfqState.Published);
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
/// <summary>
/// T3-36. §3.1: "UnderEvaluation | Clarification | Request clarification |
/// `procurement_officer`,`evaluator` / `rfq.clarify` | Reason; targeted supplier(s)".
///
/// <para>Same shape as every other transition handler in this file - load scoped, call the
/// aggregate, catch the two refusal kinds, audit with from/to states, save. Deliberately not a new
/// pattern: three states becoming reachable is a gap in the machine, not a reason to invent a second
/// way of moving through it.</para>
/// </summary>
public sealed class RequestRfqClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IRequestRfqClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(RequestRfqClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RequestClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Clarification);
        }

        // §3.1's notification column: "Email + in-app to targeted supplier". The invitees are who the
        // clarification is addressed to, and EPIC-15's catalogue is where the words live.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqClarificationRequested,
            await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
            $"{NotificationTypes.RfqClarificationRequested}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_requested", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.UnderEvaluation),
            toState: nameof(RfqState.Clarification), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

/// <summary>T3-36. §3.1: "Clarification | UnderEvaluation | Clarification resolved".</summary>
public sealed class ResolveRfqClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IResolveRfqClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(ResolveRfqClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.ResolveClarification();
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.UnderEvaluation);
        }

        // §3.1: "In-app to committee".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqClarificationResolved,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.RfqClarificationResolved}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_resolved", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Clarification),
            toState: nameof(RfqState.UnderEvaluation), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

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
            return RfqTransitions.Refusal(rfq, ex, RfqState.SubmissionClosed);
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
            return RfqTransitions.Refusal(rfq, ex, RfqState.Cancelled);
        }

        // A-9/BRULE-056, enforced for the first time. The rule says cancellation "voids open
        // invitations/proposals"; before this it notified everyone and moved nothing, so a Submitted
        // proposal stayed Submitted forever on a cancelled tender - and BRULE-056 carries no assumption
        // tag, which made that a confirmed rule going unenforced.
        //
        // Terminal proposals are left alone: a withdrawn bid was withdrawn, and an awarded one belongs
        // to an RFQ that could not have been cancelled.
        var liveProposals = await db.Proposals.Where(p => p.RfqId == rfq.Id).ToListAsync(ct);
        foreach (var proposal in liveProposals.Where(p => Proposal.AllowedNextFrom(p.State).Count > 0))
        {
            proposal.CancelWithRfq();

            NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalCancelled,
                await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
                $"{NotificationTypes.ProposalCancelled}:{proposal.Id}",
                new Dictionary<string, string?>
                {
                    ["rfqCode"] = rfq.ReferenceCode,
                    ["proposalCode"] = proposal.ReferenceCode,
                    ["proposalId"] = proposal.Id.ToString(),
                });

            await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_cancelled", scope.UserId,
                referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Cancelled),
                reason: command.Reason, ct: ct);
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
            return RfqTransitions.Refusal(rfq, ex);
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
            rfq.AnswerClarification(command.ClarificationId, command.Answer);
            clarification = rfq.Clarifications.Single(c => c.Id == command.ClarificationId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_answered", scope.UserId, referenceCode: rfq.ReferenceCode,
            changes: $"{{\"clarificationId\":\"{command.ClarificationId}\",\"published\":true}}", ct: ct);
        await db.SaveChangesAsync(ct);

        // A-4: the asker is told their own question was answered; every OTHER invitee is told an
        // answer was published. Two different messages because they are two different facts, and the
        // second one must not name the asker.
        await NotifyAskerAsync(db, backgroundJobs, clarification.AskedBySupplierId, rfq.Id, clarification.Id, ct);
        await NotifyOtherInviteesAsync(db, backgroundJobs, rfq, clarification.AskedBySupplierId, ct);

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
            return RfqTransitions.Refusal(rfq, ex);
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
            return RfqTransitions.Refusal(rfq, ex);
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

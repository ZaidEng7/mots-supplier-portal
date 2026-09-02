using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-08.6/FR-INV-006: the supplier-facing self-service side of RFQ Invitations - the
/// security boundary this feature exists for. Every handler here resolves the RFQ by
/// (SupplierId, ReferenceCode) through a real Invitation row, never by ReferenceCode alone: a
/// non-invited supplier's request finds no row and gets the same NotFoundOrNotInvited a wrong
/// reference code would, so the two cases are indistinguishable from outside (no oracle for
/// "does this RFQ exist").</summary>
/// <summary>Internal (not file-scoped) so EPIC-09's ProposalHandlers.cs can reuse LoadInvitedAsync
/// for "is this caller invited to this RFQ" rather than reimplementing the same check.</summary>
internal static class SupplierRfqLoader
{
    /// <summary>Also requires the RFQ to be Published or later - Draft/InternalReview/Approved
    /// RFQs are buyer-internal even for an already-invited supplier (invitations can be created
    /// starting in Draft per FEAT-08.1/candidate-identification, but visibility only opens at
    /// Publish, matching BUSINESS-PROCESSES.md's "Approved -&gt; Published: generate access").</summary>
    public static async Task<(Rfq Rfq, Invitation Invitation)?> LoadInvitedAsync(
        AppDbContext db, IScopeContext scope, string referenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var rfq = await db.Rfqs
            .Include(r => r.Items).Include(r => r.Requirements).Include(r => r.Attachments).Include(r => r.Invitations)
            .Include(r => r.Clarifications).Include(r => r.Addenda)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode, ct);
        if (rfq is null || rfq.State is RfqState.Draft or RfqState.InternalReview or RfqState.Approved) return null;

        var invitation = rfq.Invitations.FirstOrDefault(i => i.SupplierId == scope.SupplierId.Value);
        return invitation is null ? null : (rfq, invitation);
    }
}

public sealed class SupplierListInvitedRfqsHandler(AppDbContext db, IScopeContext scope) : ISupplierListInvitedRfqsHandler
{
    public async Task<ListEnvelope<SupplierRfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct)
    {
        var size = ListEnvelope<SupplierRfqListItemDto>.ClampPageSize(pageSize);
        if (scope.SupplierId is null) return ListEnvelope<SupplierRfqListItemDto>.Empty(size);

        var supplierId = scope.SupplierId.Value;

        // The invitation-scoping predicate and the pre-Published exclusion are UNCHANGED, and are
        // applied before the cursor narrows the set - so they hold identically on every page, not
        // just the first. CrossOrganizationScopeTests' paging test exists to prove exactly that.
        var query = db.Rfqs
            .Where(r => r.Invitations.Any(i => i.SupplierId == supplierId)
                && r.State != RfqState.Draft && r.State != RfqState.InternalReview && r.State != RfqState.Approved);

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

        // MyInvitationStatus is resolved in SQL by a correlated subquery over this supplier's own
        // invitation row - the Invitations collection is never loaded, so the previous
        // `r.Invitations.Single(...)` in-memory filter is gone along with the include.
        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                // §12-A/D: §12.4's documented list fields. Every one is a correlated subquery or a
                // scalar on the row - NOTHING here loads a child collection. That is the whole point
                // of Batch 0.2's projection work: `r.Items.Count()` becomes a COUNT in SQL, not a
                // materialised Items list whose Count is then read in memory.
                Dto = new SupplierRfqListItemDto(
                    r.ReferenceCode, r.TitleAr, r.TitleEn, r.State,
                    r.Invitations.Where(i => i.SupplierId == supplierId).Select(i => i.Status).FirstOrDefault(),
                    r.CreatedAt,
                    r.PublishedAt,
                    db.Organizations.Where(o => o.Id == r.OrganizationId)
                        .Select(o => new BuyingOrgDto(o.ExternalId, o.LegalNameEn)).FirstOrDefault(),
                    r.Items.Count(),
                    db.Proposals.Any(pr => pr.RfqId == r.Id
                        && pr.SupplierId == supplierId
                        && pr.State == ProposalState.Draft)),
            })
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        var items = hasMore ? rows[..size] : rows;

        return ListEnvelope<SupplierRfqListItemDto>.Cursor(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new RfqListCursor(items[^1].Dto.CreatedAt, items[^1].Id).Encode() : null,
            size,
            totalCount,
            sort: "-createdAt");
    }
}

/// <summary>Marks the invitation Viewed as a side effect of a successful fetch (FEAT-08.6) - the
/// first time an invited supplier actually opens the RFQ, not merely lists it.</summary>
public sealed class SupplierGetRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISupplierGetRfqHandler
{
    public async Task<SupplierRfqResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, referenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        var wasFirstView = invitation.Status == InvitationStatus.Invited;
        rfq.MarkInvitationViewed(scope.SupplierId!.Value);
        if (wasFirstView)
        {
            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_viewed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
        }

        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}

public sealed class SupplierDeclineInvitationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ISupplierDeclineInvitationHandler
{
    public async Task<SupplierRfqResult> HandleAsync(DeclineInvitationCommand command, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, command.ReferenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        try
        {
            rfq.DeclineInvitation(scope.SupplierId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new SupplierRfqResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_declined", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}

/// <summary>FEAT-10.1/FR-CLR-001/FR-CLR-005: only an actually-invited supplier can post, and only
/// within the clarification window - both enforced through the same SupplierRfqLoader every other
/// supplier-facing action uses, not a reimplementation. FEAT-10.6: audited and notified (the buyer
/// side sees the new question on next dashboard fetch, same "in-app" convention as invitations -
/// no per-RFQ buyer-contact concept exists to email against).</summary>
public sealed class SupplierPostClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : ISupplierPostClarificationHandler
{
    public async Task<SupplierRfqResult> HandleAsync(PostClarificationQuestionCommand command, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, command.ReferenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        Clarification clarification;
        try
        {
            clarification = rfq.PostClarificationQuestion(scope.SupplierId!.Value, command.Question);
        }
        catch (DomainException ex)
        {
            return new SupplierRfqResult.InvalidState(ex.Message);
        }

        db.Clarifications.Add(clarification);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_posted", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"clarificationId\":\"{clarification.Id}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var officerUserIds = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join u in db.Users on ur.UserId equals u.Id
            where r.Name == Domain.Identity.Roles.ProcurementOfficer && u.OrganizationId == rfq.OrganizationId
            select u.Id)
            .Distinct()
            .ToListAsync(ct);
        foreach (var userId in officerUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationPostedEmailAsync(userId, rfq.Id, clarification.Id, CancellationToken.None));
        }

        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}

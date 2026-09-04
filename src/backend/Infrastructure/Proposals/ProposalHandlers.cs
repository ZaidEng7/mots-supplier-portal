using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Infrastructure.Proposals;

internal static class ProposalDtoMapper
{
    /// <summary>The ONLY place ProposalItemDto (financial envelope) is ever produced. Every handler
    /// in this file resolves the caller's own Proposal by their own SupplierId first (see
    /// ProposalLoader below) - there is no code path anywhere that builds this DTO for a proposal
    /// that is not the caller's own. That is the two-envelope seal for this epic: not a filter
    /// applied to a shared read, but the simple fact that no other read exists yet.</summary>
    public static ProposalDto ToDto(Proposal proposal, string rfqReferenceCode) => new(
        proposal.ReferenceCode, rfqReferenceCode, proposal.State,
        proposal.CurrencyCode, proposal.PaymentTerms, proposal.IncotermCode, proposal.DeliveryTermsAr, proposal.DeliveryTermsEn,
        proposal.Warranty, proposal.ValidityStart, proposal.ValidityEnd,
        proposal.NarrativeAr, proposal.NarrativeEn,
        proposal.SubmittedAt, proposal.WithdrawnAt, proposal.WithdrawReason,
        [.. proposal.Items.Select(i => new ProposalItemDto(i.Id, i.RfqItemId, i.Quantity, i.UnitPrice, i.Discount, i.LineTotal, i.LeadTimeDays, i.NotesAr, i.NotesEn))],
        [.. proposal.Documents.Select(d => new ProposalDocumentDto(d.Id, d.OriginalFileName, d.ContentType, d.Caption, d.UploadedAt))],
        [.. proposal.RequirementAnswers.Select(a => new RequirementAnswerDto(a.Id, a.RequirementId, a.AnswerAr, a.AnswerEn))],
        proposal.RowVersion);
}

/// <summary>Resolves (Rfq, Invitation, Proposal?) for the caller's own SupplierId - reuses
/// SupplierRfqLoader.LoadInvitedAsync (EPIC-08) for the invitation check rather than
/// reimplementing it, per this epic's own instruction.</summary>
internal static class ProposalLoader
{
    public static async Task<(Rfq Rfq, Proposal? Proposal)?> LoadAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;
        var (rfq, _) = loaded.Value;

        // A supplier may now have more than one proposal on an RFQ: BUSINESS-PROCESSES.md §4.1
        // permits re-entry after a withdrawal, and names its mechanism as a NEW DRAFT. So the LIVE
        // proposal wins over a withdrawn one, and a withdrawn one is still returned when it is all
        // there is - a supplier who withdrew and has not restarted should still see that they
        // withdrew, rather than a screen that behaves as though they were never here.
        var proposal = await db.Proposals
            .Include(p => p.Items).Include(p => p.Documents).Include(p => p.RequirementAnswers)
            .AsSplitQuery()
            .Where(p => p.RfqId == rfq.Id && p.SupplierId == scope.SupplierId!.Value)
            .OrderBy(p => p.State == ProposalState.Withdrawn ? 1 : 0)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return (rfq, proposal);
    }

    /// <summary>
    /// §12-A/C2: resolve a proposal by its OWN public code, for the relocated
    /// <c>/api/v1/proposals/{proposalCode}</c> routes (§3: <c>/proposals/{proposalCode}/items</c>,
    /// §12.5: <c>PATCH /proposals/{proposalCode}</c>).
    ///
    /// <para><b>The row-scope predicate is the whole point.</b> The old path could not name another
    /// supplier's proposal - <c>/suppliers/me/rfqs/{code}/proposal</c> has no slot for one. This one
    /// can, so <c>SupplierId == scope.SupplierId</c> is applied IN THE QUERY rather than checked
    /// afterwards, and a miss is indistinguishable from a code that never existed. §9.2:
    /// *"Out-of-scope access to an existing resource returns 404 (not 403) to avoid leaking
    /// existence"* - so this returns null for both cases and the endpoint maps null to 404.</para>
    /// </summary>
    public static async Task<(Rfq Rfq, Proposal Proposal)?> LoadByProposalCodeAsync(
        AppDbContext db, IScopeContext scope, string proposalReferenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var proposal = await db.Proposals
            .Include(p => p.Items).Include(p => p.Documents).Include(p => p.RequirementAnswers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                p => p.ReferenceCode == proposalReferenceCode && p.SupplierId == scope.SupplierId!.Value, ct);
        if (proposal is null) return null;

        // Items and Requirements are NOT optional here. Submit-completeness asks "is every required
        // RFQ item priced, is every mandatory requirement answered" by walking these collections,
        // so loading the RFQ bare makes both checks vacuously TRUE and lets an incomplete proposal
        // submit. The RFQ-keyed loader this replaces got them from SupplierRfqLoader's includes;
        // dropping them here was silent, and two existing completeness tests caught it.
        var rfq = await db.Rfqs
            .Include(r => r.Items)
            .Include(r => r.Requirements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == proposal.RfqId, ct);
        return rfq is null ? null : (rfq, proposal);
    }
}

/// <summary>FEAT-09.1/FR-PRP-001, BUSINESS-PROCESSES.md §4.1: Active + Invitation are checked here
/// (cross-aggregate, same split as InviteSupplierHandler's own Active check); uniqueness is
/// idempotent - a second start returns the existing Draft rather than erroring, per FEAT-09.1's own
/// AC, with the DB unique(rfq_id, supplier_id) index as the real race-safe guarantee underneath.</summary>
public sealed class GetProposalByCodeHandler(AppDbContext db, IScopeContext scope) : IGetProposalByCodeHandler
{
    public async Task<ProposalResult> HandleAsync(string proposalReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        return loaded is null
            ? new ProposalResult.NotFoundOrNotInvited()
            : new ProposalResult.Success(ProposalDtoMapper.ToDto(loaded.Value.Proposal, loaded.Value.Rfq.ReferenceCode));
    }
}

public sealed class StartProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IStartProposalHandler
{
    public async Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, existing) = loaded.Value;

        // Start stays idempotent for a LIVE proposal - a second click returns the same draft rather
        // than making another. A WITHDRAWN one is different: BUSINESS-PROCESSES.md §4.1 permits
        // "re-submission allowed while window open (new draft)", so a withdrawal is not a bar to
        // starting again, it is the absence of a current proposal.
        //
        // Before this, a withdrawn proposal was returned here as though it were the supplier's
        // current one - and every edit path then refused it, because it is not a Draft. A supplier
        // who withdrew to correct a price could never bid on that RFQ again, silently and
        // permanently. The withdrawal window guard is unchanged: this only applies while the RFQ is
        // SubmissionOpen, because SupplierRfqLoader.LoadInvitedAsync is what got us here.
        if (existing is not null && existing.State != ProposalState.Withdrawn)
        {
            return new ProposalResult.Success(ProposalDtoMapper.ToDto(existing, rfq.ReferenceCode));
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == scope.SupplierId!.Value, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new ProposalResult.NotFoundOrNotInvited();
        }

        var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "PRP", ct);
        var proposal = Proposal.Create(referenceCode, rfq.Id, scope.SupplierId!.Value);
        db.Proposals.Add(proposal);
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_started", scope.UserId, referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Draft), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

public sealed class GetProposalHandler(AppDbContext db, IScopeContext scope) : IGetProposalHandler
{
    public async Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadAsync(db, scope, rfqReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal!, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-09.1/FR-PRP-002: the financial envelope. Nothing here differs structurally from any
/// other Draft-only edit handler - the envelope separation lives in the schema/DTO layer (see
/// ProposalDtoMapper's own doc comment), not in extra guards on writes.</summary>
public sealed class ManageProposalItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageProposalItemHandler
{
    public async Task<ProposalResult> SetAsync(SetItemPricingCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.SetItemPricing(command.RfqItemId, command.Quantity, command.UnitPrice, command.Discount, command.LeadTimeDays, command.NotesAr, command.NotesEn);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        db.ProposalItems.Add(proposal.Items.First(i => i.RfqItemId == command.RfqItemId));
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_item_priced", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }

    public async Task<ProposalResult> RemoveAsync(RemoveItemPricingCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.RemoveItemPricing(command.RfqItemId);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_item_removed", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

public sealed class SetCommercialTermsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISetCommercialTermsHandler
{
    public async Task<ProposalResult> HandleAsync(SetCommercialTermsCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.SetCommercialTerms(command.CurrencyCode, command.PaymentTerms, command.IncotermCode,
                command.DeliveryTermsAr, command.DeliveryTermsEn, command.Warranty, command.ValidityStart, command.ValidityEnd);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_terms_updated", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

public sealed class SetNarrativeHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISetNarrativeHandler
{
    public async Task<ProposalResult> HandleAsync(SetNarrativeCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.SetNarrative(command.NarrativeAr, command.NarrativeEn);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_narrative_updated", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

public sealed class AnswerRequirementHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IAnswerRequirementHandler
{
    public async Task<ProposalResult> HandleAsync(AnswerRequirementCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.AnswerRequirement(command.RequirementId, command.AnswerAr, command.AnswerEn);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        db.RequirementAnswers.Add(proposal.RequirementAnswers.First(a => a.RequirementId == command.RequirementId));
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_requirement_answered", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-09.3/FR-PRP-004: stored via IFileStorage directly, same convention as
/// RfqAttachment (no AV-scan quarantine flow here either - OQ-014 already tags AV scanning
/// generally as [REQUIRES BUSINESS CONFIRMATION], same deliberate scope decision as RFQ
/// attachments).</summary>
public sealed class ManageProposalDocumentHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageProposalDocumentHandler
{
    public async Task<ProposalResult> AddAsync(AddProposalDocumentCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        ProposalDocument document;
        try
        {
            document = proposal!.AddDocument(command.StorageKey, command.OriginalFileName, command.ContentType, command.Caption);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        db.ProposalDocuments.Add(document);
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_document_added", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }

    public async Task<ProposalResult> RemoveAsync(RemoveProposalDocumentCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.RemoveDocument(command.DocumentId);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_document_removed", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-09.5/FR-PRP-006/007, the safety-critical endpoint: submissionCloseAt and the
/// required/mandatory id sets are resolved from the loaded Rfq here and handed to
/// Proposal.Submit as plain facts - the domain method (not this handler) is what actually refuses
/// a late submission, using the server's own clock. See Proposal.Submit's own doc comment for the
/// two flagged ambiguities (mandatory-document gating, RFQ minimum validity) this cannot enforce
/// without an invented number.</summary>
public sealed class SubmitProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : ISubmitProposalHandler
{
    public async Task<ProposalResult> HandleAsync(SubmitProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        if (rfq.SubmissionClosesAt is null) return new ProposalResult.InvalidState("This RFQ has no submission close date set.");
        var requiredItemIds = rfq.Items.Where(i => !i.IsOptional).Select(i => i.Id).ToHashSet();
        var mandatoryRequirementIds = rfq.Requirements.Where(r => r.IsMandatory).Select(r => r.Id).ToHashSet();

        // T-065: captured BEFORE the call, and attached below only when the refusal is genuinely
        // about state. Submit throws for two different reasons - a wrong source state, and an
        // incomplete proposal - and §12.5 gives those different answers: 409 for the first, 422
        // (PROPOSAL_ITEMS_REQUIRED) for the second. Mapping both to 409 would tell a supplier with
        // an unpriced item that their proposal had moved on.
        var submittableState = proposal!.State == ProposalState.Draft;

        try
        {
            proposal.Submit(rfq.State == RfqState.SubmissionOpen, rfq.SubmissionClosesAt.Value, requiredItemIds, mandatoryRequirementIds);
        }
        catch (DomainException ex)
        {
            // Completeness and window refusals keep the 400 they had. §12.5's 422 for missing items
            // is a real, separate divergence and is recorded as T-066 rather than guessed at here.
            return submittableState
                ? new ProposalResult.InvalidState(ex.Message)
                : new ProposalResult.InvalidState(ex.Message, proposal.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_submitted", scope.UserId, referenceCode: proposal.ReferenceCode,
            fromState: nameof(ProposalState.Draft), toState: nameof(ProposalState.Submitted), ct: ct);
        await db.SaveChangesAsync(ct);

        if (scope.UserId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendProposalSubmittedEmailAsync(scope.UserId.Value, proposal.Id, CancellationToken.None));
        }

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

public sealed class WithdrawProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IWithdrawProposalHandler
{
    public async Task<ProposalResult> HandleAsync(WithdrawProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        var fromState = proposal!.State;
        try
        {
            proposal.Withdraw(command.Reason, rfq.State == RfqState.SubmissionOpen);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        // §3.2 "Draft / Submitted -> Withdrawn | In-app to supplier + procurement". Two groups: the
        // supplier's own users (so a colleague sees the withdrawal) and the RFQ's committee.
        var withdrawRecipients = await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct);
        withdrawRecipients.AddRange(await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct));
        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalWithdrawn, withdrawRecipients,
            $"{NotificationTypes.ProposalWithdrawn}:{proposal.Id}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_withdrawn", scope.UserId, referenceCode: proposal.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(ProposalState.Withdrawn), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

/// <summary>
/// T-051, §4.1: <c>UnderReview -&gt; ClarificationRequested</c>. Buyer-side - the proposal is loaded
/// through the RFQ's own scope, not the supplier's, because the actor here is procurement.
/// </summary>
public sealed class RequestProposalClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IRequestProposalClarificationHandler
{
    public async Task<ProposalResult> HandleAsync(RequestProposalClarificationCommand command, CancellationToken ct)
    {
        var proposal = await db.Proposals
            .FirstOrDefaultAsync(p => p.ReferenceCode == command.ProposalReferenceCode, ct);
        if (proposal is null) return new ProposalResult.NotFoundOrNotInvited();

        // Row scope IN the query: the proposal must belong to an RFQ in the caller's organization,
        // and a miss is indistinguishable from a code that never existed (§9.2).
        var rfq = await db.Rfqs.FirstOrDefaultAsync(
            r => r.Id == proposal.RfqId && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return new ProposalResult.NotFoundOrNotInvited();

        var fromState = proposal.State;
        try
        {
            proposal.RequestClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        // §4.1: "Email + in-app to supplier".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalClarificationRequested,
            await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
            $"{NotificationTypes.ProposalClarificationRequested}:{proposal.Id}:{proposal.RevisionNumber}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_clarification_requested", scope.UserId,
            referenceCode: proposal.ReferenceCode, reason: command.Reason,
            fromState: fromState.ToString(), toState: nameof(ProposalState.ClarificationRequested), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

/// <summary>
/// T-051, §4.1: <c>ClarificationRequested -&gt; Revised</c>. Supplier-side, so it loads through the
/// supplier's own scope like every other supplier proposal action.
/// </summary>
public sealed class ReviseProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IReviseProposalHandler
{
    public async Task<ProposalResult> HandleAsync(ReviseProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        var fromState = proposal!.State;
        try
        {
            proposal.RecordRevision();
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        // §4.1: "In-app to committee".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalRevised,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.ProposalRevised}:{proposal.Id}:{proposal.RevisionNumber}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_revised", scope.UserId,
            referenceCode: proposal.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(ProposalState.Revised), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}

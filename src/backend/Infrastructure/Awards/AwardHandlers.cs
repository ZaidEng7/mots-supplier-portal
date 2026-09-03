using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Awards;

internal static class AwardDtoMapper
{
    public static AwardDto ToDto(Award award, string rfqReferenceCode) => new(
        award.Id, rfqReferenceCode, award.State,
        award.WinningProposalId, award.JustificationAr, award.JustificationEn,
        award.RecommendedByUserId, award.RecommendedAt, award.RecommendationRevision,
        [.. award.Approvals.Select(a => new AwardApprovalDto(a.StepNo, a.ApproverUserId, a.Decision, a.Comment, a.DecidedAt))],
        award.AwardedAt, award.ComparisonSnapshotJson,
        award.ErpSyncStatus, award.ExternalPurchaseOrderRef, award.ErpSyncedAt, award.ErpRetryCount,
        award.RowVersion);
}

file static class AwardLoader
{
    public static IQueryable<Award> IncludeAll(this DbSet<Award> set) => set.Include(a => a.Approvals);

    public static async Task<(Rfq Rfq, Award? Award)?> LoadScopedAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var award = await db.Awards.IncludeAll().FirstOrDefaultAsync(a => a.RfqId == rfq.Id, ct);
        return (rfq, award);
    }
}

public sealed class GetAwardHandler(AppDbContext db, IScopeContext scope) : IGetAwardHandler
{
    public async Task<AwardDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, rfqReferenceCode, ct);
        return loaded is null || loaded.Value.Award is null ? null : AwardDtoMapper.ToDto(loaded.Value.Award, loaded.Value.Rfq.ReferenceCode);
    }
}

/// <summary>FEAT-14.1/FR-AWD-001, BRULE-071: "recorded only after evaluation is Finalized and the
/// recommended proposal passes all thresholds" - both cross-aggregate facts (Evaluation lives in
/// its own aggregate), resolved here before calling the domain method, same split as every other
/// cross-aggregate guard in this codebase. Handles both the first recommendation (no Award row yet)
/// and a re-recommendation after Rejected (Award.ReRecommend) through the same endpoint - the table
/// names both `award.recommend`, same actor.</summary>
public sealed class RecommendAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRecommendAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(RecommendAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, existingAward) = loaded.Value;

        var evaluation = await db.Evaluations.Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        if (evaluation is null || evaluation.State != EvaluationState.Finalized)
        {
            return new AwardMutationResult.InvalidState("Cannot recommend an award: the evaluation has not been finalized.");
        }
        var result = evaluation.Results.FirstOrDefault(r => r.ProposalId == command.WinningProposalId);
        if (result is null || !result.TechnicallyQualified)
        {
            return new AwardMutationResult.InvalidState("Cannot recommend this proposal: it did not pass technical qualification in the finalized evaluation.");
        }
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == command.WinningProposalId && p.RfqId == rfq.Id, ct);
        if (proposal is null || proposal.State != ProposalState.Submitted)
        {
            return new AwardMutationResult.InvalidState("The recommended proposal is not eligible for award.");
        }

        Award award;
        string action;
        try
        {
            if (existingAward is null)
            {
                award = Award.Recommend(rfq.Id, command.WinningProposalId, command.JustificationAr, command.JustificationEn, scope.UserId!.Value);
                db.Awards.Add(award);
                action = "award.recommended";
            }
            else
            {
                award = existingAward;
                award.ReRecommend(command.WinningProposalId, command.JustificationAr, command.JustificationEn, scope.UserId!.Value);
                action = "award.re_recommended";
            }
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // §3.4 "- -> Recommended | In-app to approver" and "Rejected -> Recommended | In-app to
        // approver". The APPROVER POOL: nothing in the Identity domain resolves a single named
        // approver from the AwardApprove claim, so this notifies everyone who could approve it.
        // Reported as the open business question it is.
        NotificationOutbox.EnqueueMany(db,
            action == "award.re_recommended" ? NotificationTypes.AwardReRecommended : NotificationTypes.AwardRecommended,
            await NotificationRecipients.AwardApproversAsync(db, rfq.OrganizationId, ct),
            $"{action}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, action, scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(AwardState.Recommended), reason: null, ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-14.2/FR-AWD-002: routes the recommendation for approval and, in the same
/// handler/SaveChanges, moves the RFQ itself into AwardApproval (Rfq.EnterAwardApproval's own doc
/// comment covers why that guards on UnderEvaluation rather than a Recommendation state nothing can
/// produce yet).
///
/// <para><b>EPIC-13/FEAT-13.3 audit finding, left as a documented judgment call rather than a code
/// fix:</b> unlike RfqPublishHandler/CancelRfqHandler/AssignEvaluatorsHandler (all fixed this epic
/// to notify their real recipients), no email is enqueued here to "the approver" - because no
/// mechanism anywhere in the Identity domain resolves who that is. Permissions.AwardApprove is a
/// CLAIM held by a role (ProcurementManager), not a single identifiable user or a queryable list of
/// candidate approvers; a single-approver segregation-of-duties model (BRULE-077: approver must
/// differ from recommender) does not by itself say WHICH holder of that claim should be paged. This
/// is the same open design question EPIC-14 already flagged when Award was first built, not a new
/// gap introduced here - notifying "everyone with AwardApprove" would be a guess this codebase's own
/// audit trail conventions do not support without a real approver-assignment concept
/// (EPIC-15/notifications scope, unbuilt).</para></summary>
public sealed class RouteAwardForApprovalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRouteAwardForApprovalHandler
{
    public async Task<AwardMutationResult> HandleAsync(RouteAwardForApprovalCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        // EPIC-13/FEAT-13.2 stage-gate audit: this used to be `if (rfq.State == UnderEvaluation)
        // rfq.EnterAwardApproval();` - correct for the legitimate re-route cycle (RFQ already
        // AwardApproval from a prior Reject -> ReRecommend -> RouteForApproval pass, where a
        // second EnterAwardApproval() call would wrongly throw), but it silently no-op'd for EVERY
        // other RFQ state too - Cancelled, Awarded, Completed - letting Award.RouteForApproval()
        // succeed unconditionally regardless of RFQ state, a real cross-aggregate gap: the Award
        // could advance to PendingApproval on a dead or already-concluded RFQ. Explicitly refuse
        // every state outside the two legitimate ones instead of only handling the happy path.
        if (rfq.State is not (RfqState.UnderEvaluation or RfqState.AwardApproval))
        {
            return new AwardMutationResult.InvalidState($"Cannot route award for approval: the RFQ is in state '{rfq.State}'.");
        }

        var existingApprovalIds = award.Approvals.Select(a => a.Id).ToHashSet();
        try
        {
            award.RouteForApproval();
            if (rfq.State == RfqState.UnderEvaluation) rfq.EnterAwardApproval();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }
        // EF's change-tracker misclassifies a brand-new child appended to an already-Included
        // collection as Modified (not Added) when the SAME SaveChanges also updates the owning
        // Award row's own State column - see EPIC-11's AssignEvaluatorsHandler for the first time
        // this was found; forcing the state explicitly for the row this call actually created
        // sidesteps that misdetection rather than relying on DetectChanges' fixup heuristic.
        foreach (var approval in award.Approvals.Where(a => !existingApprovalIds.Contains(a.Id)))
        {
            db.Entry(approval).State = EntityState.Added;
        }

        // §3.4 "Recommended -> PendingApproval | Email + in-app to approver(s)".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardRoutedForApproval,
            await NotificationRecipients.AwardApproversAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.AwardRoutedForApproval}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.pending_approval", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.Recommended), toState: nameof(AwardState.PendingApproval), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-14.3/FR-AWD-003, BRULE-073/075: segregation of duties (approver != recommender) is
/// enforced HERE first - the primary, API-policy enforcement point BUSINESS-PROCESSES.md §6.1's own
/// actor column names - before Award.Approve's own domain-level repeat of the same check ever runs;
/// a distinct AwardMutationResult.SegregationOfDutiesViolation lets the API return a specific error
/// code rather than a generic domain-exception message. The winning supplier's Active status is
/// also checked here, at approval time, per BRULE-075's own "at the moment of approval" wording.</summary>
public sealed class ApproveAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IApproveAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(ApproveAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        if (scope.UserId == award.RecommendedByUserId)
        {
            return new AwardMutationResult.SegregationOfDutiesViolation();
        }
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == award.WinningProposalId, ct);
        var supplier = proposal is null ? null : await db.Suppliers.FirstOrDefaultAsync(s => s.Id == proposal.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new AwardMutationResult.SupplierNotActive();
        }

        try
        {
            award.Approve(scope.UserId!.Value);
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // §3.4 "PendingApproval -> Approved | In-app to officer".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardApproved,
            await NotificationRecipients.ProcurementOfficersAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.AwardApproved}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.approved", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.PendingApproval), toState: nameof(AwardState.Approved), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

public sealed class RejectAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRejectAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(RejectAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        if (scope.UserId == award.RecommendedByUserId)
        {
            return new AwardMutationResult.SegregationOfDutiesViolation();
        }

        try
        {
            award.Reject(scope.UserId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // §3.4 "PendingApproval -> Rejected | In-app to officer". The rejection REASON stays out of
        // the payload and out of the words (BRULE-091); the officer reads it on the award screen.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardRejected,
            await NotificationRecipients.ProcurementOfficersAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.AwardRejected}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.rejected", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.PendingApproval), toState: nameof(AwardState.Rejected), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

/// <summary>FEAT-14.4/14.5/14.6/14.7, FR-AWD-004/005/006/008: "execute award" - the whole
/// win/lose/RFQ/outbox side effect happens inside ONE SaveChangesAsync call, so there is never a
/// window where the winner is Awarded but a loser is still Submitted, or the RFQ hasn't moved yet.
/// The comparison snapshot (FEAT-14.7) is captured from the SAME IGetComparisonHandler EPIC-12
/// already built, frozen as JSON on the Award row at this exact moment - never re-queried live once
/// Awarded.</summary>
public sealed class ExecuteAwardHandler(
    AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IGetComparisonHandler comparisonHandler,
    IBackgroundJobClient backgroundJobs)
    : IExecuteAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(ExecuteAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        var comparison = await comparisonHandler.HandleAsync(rfq.ReferenceCode, ct);
        var snapshotJson = JsonSerializer.Serialize(comparison);

        try
        {
            award.ExecuteAward(snapshotJson);
            rfq.MarkAwarded();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        var proposals = await db.Proposals.Where(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted).ToListAsync(ct);
        foreach (var proposal in proposals)
        {
            // EPIC-13/FEAT-13.3 audit finding: this loop mutates every Proposal's own State but
            // previously logged nothing under "Proposal" - only the Award and Rfq rows were
            // audited, even though BUSINESS-PROCESSES.md §4.1 names proposal.awarded/
            // proposal.not_selected as their own audited events.
            if (proposal.Id == award.WinningProposalId)
            {
                proposal.Award();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.awarded", scope.UserId,
                    referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Awarded), ct: ct);
            }
            else
            {
                proposal.MarkNotSelected();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.not_selected", scope.UserId,
                    referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.NotSelected), ct: ct);
            }
        }

        db.OutboxMessages.Add(new Domain.Common.OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = "AwardApproved",
            PayloadJson = JsonSerializer.Serialize(new { AwardId = award.Id, RfqId = rfq.Id, RfqReferenceCode = rfq.ReferenceCode, award.WinningProposalId }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await auditLogger.LogAsync("Award", award.Id, "award.awarded", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.Approved), toState: nameof(AwardState.Awarded), ct: ct);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_awarded", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.AwardApproval), toState: nameof(RfqState.Awarded), ct: ct);
        await db.SaveChangesAsync(ct);

        // Notify after the state change persists (InviteSupplierHandler's own established
        // pattern). Winner gets an award notice; every non-winning supplier gets a regret notice -
        // BRULE-082: no commercial detail of the winner is ever put in the loser's email, only the
        // fact of the outcome.
        var winnerSupplierId = proposals.First(p => p.Id == award.WinningProposalId).SupplierId;
        var winnerUserId = await db.Users.Where(u => u.SupplierId == winnerSupplierId)
            .Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (winnerUserId != Guid.Empty)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAwardIssuedEmailAsync(winnerUserId, rfq.Id, CancellationToken.None));
        }
        var loserSupplierIds = proposals.Where(p => p.Id != award.WinningProposalId).Select(p => p.SupplierId).ToList();
        var loserUserIds = await db.Users.Where(u => u.SupplierId != null && loserSupplierIds.Contains(u.SupplierId.Value)).Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in loserUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAwardRegretEmailAsync(userId, rfq.Id, CancellationToken.None));
        }

        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

public sealed class RetryErpSyncHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRetryErpSyncHandler
{
    public async Task<AwardMutationResult> HandleAsync(RetryErpSyncCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        try
        {
            award.RetryErpSync();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Award", award.Id, "award.erp_po_retried", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(ErpSyncStatus.Requested), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode));
    }
}

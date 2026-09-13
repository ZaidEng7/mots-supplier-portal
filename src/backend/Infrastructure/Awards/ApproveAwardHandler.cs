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

            // T-064/§4.1: "Shortlisted -> AwardOffered | Selected for award ... Mark as award
            // candidate | Email + in-app to supplier (offer)". Approve is the first point at which the
            // offer is TRUE - see Proposal.OfferAward on why it is not made at recommend time.
            //
            // Only from Shortlisted. An RFQ that never shortlisted awards directly at execute, the
            // path that already existed; forcing every award through the offer would break those, and
            // §4.1 does not require it.
            // proposal is non-null here: the SupplierNotActive guard above returns early when it is,
            // so reaching this line means both the proposal and its supplier were found.
            if (proposal!.State == ProposalState.Shortlisted)
            {
                proposal.OfferAward();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.award_offered", scope.UserId,
                    referenceCode: proposal.ReferenceCode,
                    fromState: nameof(ProposalState.Shortlisted), toState: nameof(ProposalState.AwardOffered), ct: ct);

                NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalAwardOffered,
                    await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
                    $"{NotificationTypes.ProposalAwardOffered}:{proposal.Id}",
                    new Dictionary<string, string?>
                    {
                        ["rfqCode"] = rfq.ReferenceCode,
                        ["proposalCode"] = proposal.ReferenceCode,
                    });
            }
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // §3.4 "PendingApproval -> Approved | In-app to officer" - A-7: the RFQ's owner.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardApproved,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.AwardApproved}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.approved", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.PendingApproval), toState: nameof(AwardState.Approved), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}

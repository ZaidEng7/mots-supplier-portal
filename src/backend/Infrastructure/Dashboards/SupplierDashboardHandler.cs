using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Dashboards;

/// <summary>
/// SCR-120 / FR-DSH-008. The supplier's front door.
///
/// <para><b>Scoped to one SupplierId, in every clause.</b> Simpler than the buyer side, but the same
/// rule: a count leaks as surely as a row, so "Open invitations: 3" that included another supplier's
/// invitation would disclose volume without disclosing anything nameable. The supplier predicate is
/// the first clause of every query below and the tests assert the numbers.</para>
/// </summary>
public sealed class SupplierDashboardHandler(AppDbContext db, IScopeContext scope) : ISupplierDashboardHandler
{
    /// <summary>§1's "top 5" for both lists.</summary>
    private const int TopN = 5;

    /// <summary>
    /// "Invitations closing soon" has no documented window. Seven days, matching the buyer side's
    /// "Closing this week" - an INVENTION, but a consistent one: the same RFQ should not be urgent on
    /// one dashboard and not the other.
    /// </summary>
    private static readonly TimeSpan ClosingSoon = TimeSpan.FromDays(7);

    public async Task<SupplierDashboardDto?> HandleAsync(CancellationToken ct)
    {
        // §9.2: no supplier scope, no dashboard, and not-found rather than an empty one.
        if (scope.SupplierId is not { } supplierId) return null;

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null) return null;

        var now = DateTimeOffset.UtcNow;

        var invitations = db.Invitations.AsNoTracking().Where(i => i.SupplierId == supplierId);
        var proposals = db.Proposals.AsNoTracking().Where(p => p.SupplierId == supplierId);
        var documents = db.SupplierDocuments.AsNoTracking().Where(d => d.SupplierId == supplierId && d.IsLatestVersion);

        var kpis = new SupplierKpisDto(
            // "Open" is an invitation not yet answered - declined and submitted are both closed, from
            // the supplier's point of view, and neither is something to act on.
            OpenInvitations: await invitations.CountAsync(
                i => i.Status != InvitationStatus.Declined && i.Status != InvitationStatus.Submitted, ct),
            DraftProposals: await proposals.CountAsync(p => p.State == ProposalState.Draft, ct),
            SubmittedProposals: await proposals.CountAsync(p => p.State == ProposalState.Submitted, ct),
            DocumentsNeedingAttention: await documents.CountAsync(
                d => d.State == DocumentState.Rejected
                     || d.State == DocumentState.ExpiringSoon
                     || d.State == DocumentState.Expired, ct));

        var actionRequired = new ActionRequiredDto(
            ExpiringDocuments: await documents.CountAsync(
                d => d.State == DocumentState.ExpiringSoon || d.State == DocumentState.Expired, ct),
            RejectedDocuments: await documents.CountAsync(d => d.State == DocumentState.Rejected, ct),
            InvitationsClosingSoon: await invitations.CountAsync(
                i => i.Status != InvitationStatus.Declined && i.Status != InvitationStatus.Submitted
                     && db.Rfqs.Any(r => r.Id == i.RfqId
                                         && r.SubmissionClosesAt != null
                                         && r.SubmissionClosesAt >= now
                                         && r.SubmissionClosesAt <= now + ClosingSoon), ct),
            // A clarification this supplier asked that now has an answer. §1 lists it as an
            // action-required condition because the answer may change what they bid.
            ClarificationsAnswered: await db.Clarifications.AsNoTracking()
                .CountAsync(c => c.AskedBySupplierId == supplierId && c.Answer != null, ct),
            AwardOffers: await proposals.CountAsync(p => p.State == ProposalState.AwardOffered, ct));

        var invitationRows = await invitations
            .Select(i => new
            {
                i.RfqId, i.Status,
                Rfq = db.Rfqs.Where(r => r.Id == i.RfqId)
                    .Select(r => new { r.ReferenceCode, r.TitleAr, r.TitleEn, r.SubmissionClosesAt }).First(),
            })
            // Soonest deadline first; no-deadline rows last rather than first.
            .OrderBy(i => i.Rfq.SubmissionClosesAt == null)
            .ThenBy(i => i.Rfq.SubmissionClosesAt)
            .Take(TopN)
            .ToListAsync(ct);

        var proposalRows = await proposals
            .Where(p => p.State != ProposalState.Withdrawn && p.State != ProposalState.NotSelected)
            .Select(p => new
            {
                p.ReferenceCode, p.State, p.ValidityEnd,
                Rfq = db.Rfqs.Where(r => r.Id == p.RfqId)
                    .Select(r => new { r.ReferenceCode, r.TitleAr, r.TitleEn }).First(),
            })
            .OrderBy(p => p.ValidityEnd == null)
            .ThenBy(p => p.ValidityEnd)
            .Take(TopN)
            .ToListAsync(ct);

        var missing = await DocumentCompletenessEvaluator.GetMissingRequiredDocumentTypeCodesAsync(db, supplierId, ct);
        var requiredTotal = await db.DocumentTypes.AsNoTracking().CountAsync(t => t.IsRequired && t.IsActive, ct);
        var supplied = requiredTotal - missing.Count;

        return new SupplierDashboardDto(
            supplier.ReferenceCode, supplier.DisplayNameAr, supplier.DisplayNameEn,
            supplier.OnboardingState.ToString(), supplier.LifecycleState.ToString(),
            // §1's "Not-yet-approved" branch. Approved onboarding is the gate for being invited at
            // all, so it is the gate for this screen meaning anything.
            IsApproved: supplier.OnboardingState == SupplierOnboardingState.Approved,
            kpis, actionRequired,
            [.. invitationRows.Select(i => new DashboardInvitationDto(
                i.Rfq.ReferenceCode, i.Rfq.TitleAr, i.Rfq.TitleEn, i.Status.ToString(), i.Rfq.SubmissionClosesAt))],
            [.. proposalRows.Select(p => new DashboardProposalDto(
                p.ReferenceCode, p.Rfq.ReferenceCode, p.Rfq.TitleAr, p.Rfq.TitleEn, p.State.ToString(), p.ValidityEnd))],
            new ProfileHealthDto(
                // Guard the divide: a tenant with no required document types is complete rather than
                // NaN, which would reach the meter as a blank bar with no explanation.
                Completeness: requiredTotal == 0 ? 1 : (double)supplied / requiredTotal,
                requiredTotal, supplied,
                NextRequiredDocumentTypeCode: missing.FirstOrDefault()),
            // §1's ERP-degraded banner, from this supplier's own award only - a failure on someone
            // else's award is not this supplier's business and would leak that it exists.
            ErpDegraded: await db.Awards.AsNoTracking().AnyAsync(
                a => a.ErpSyncStatus == ErpSyncStatus.Failed
                     && db.Proposals.Any(p => p.Id == a.WinningProposalId && p.SupplierId == supplierId), ct));
    }
}

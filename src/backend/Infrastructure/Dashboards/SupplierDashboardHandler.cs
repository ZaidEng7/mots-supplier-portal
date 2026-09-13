// The supplier's front door.
//
// Scoped to one company in every clause. Simpler than the buyer side but the same rule: a count leaks as surely as
// a row, so a tile reading three open invitations that included another supplier's would disclose volume without
// disclosing anything nameable. The tests assert the numbers.
//
// No supplier scope means no dashboard, reported as a not-found rather than an empty one.
//
//
// IT LOADS THE PROFILE'S CHILDREN, AND MUST
//
// The missing-fields list reads addresses, categories and representatives, and on a record loaded bare those
// collections are EMPTY, so it reports fields missing that the supplier has actually filled in and the completeness
// meter reads lower than the truth.
//
// Caught by the test asserting this handler and the profile read return the same number: they returned different
// ones. This is the trap the shared include already warns about, hit again the moment a second caller started
// asking the record a question about its children.
//
//
// ONE DEFINITION OF COMPLETENESS, AND IT IS THE SUBMIT GATE'S
//
// The meter used to be documents supplied over documents total, which omitted the profile fields entirely, so a
// supplier with every document and no legal information read as fully complete and was then refused at submission.
//
// Two definitions of one number is how they drift, and that was the drift: the meter said ready and the gate said
// no. One evaluator now, and it is the gate's own checklist. Its denominator is the set THIS supplier is required
// to hold, resolved by the same function the gate asks.
//
// The separate document counts stay document counts, because they feed a caption that is about documents
// specifically and is still true of them.
//
// The next required document's NAME is resolved as well as its code, because the caption on that panel is the one
// line telling a supplier what to do next and it was showing them a database value. Looked up here rather than
// mapped in the interface, because the names live in the reference table and a second copy would drift the first
// time one is corrected on the administration screen.
//
//
// WHAT COUNTS AS SOMETHING TO ACT ON
//
// An open invitation is one not yet answered. Declined and submitted are both closed from the supplier's point of
// view, and neither is something to act on.
//
// A draft the window closed on is no longer a draft, so the tile stops counting a bid the supplier can never
// submit. That was the visible half of a rule going unenforced.
//
// A clarification this supplier asked that now has an answer is action-required, because the answer may change what
// they bid.
//
// The approved-onboarding branch is the gate for being invited at all, so it is the gate for this screen meaning
// anything.
//
//
// LOSING IS AN OUTCOME
//
// The bid list deliberately excludes unsuccessful bids, so a supplier who lost saw their bid vanish from this
// screen with no outcome on it anywhere. A widget that showed only wins would be a scoreboard rather than a record.
//
// The value shown is the supplier's own priced total, the number they typed, so no two-envelope question arises,
// and the decision date comes from the award record when there is one.
//
// A bid that was never priced has no total, which is absent rather than zero: zero is a number somebody quoted.
//
// Most recently decided first, with undated rows last rather than first. An undated row at the top reads as the
// newest, which is the opposite of what is known about it.
//
//
// THE TOTALS ARE SUMMED IN MEMORY OVER THE FEW ROWS ABOVE
//
// A line total is computed by the record rather than stored, so summing it in the database does not translate, and
// the mapper says so by name. The dashboard answered with a server error.
//
// Re-deriving quantity times price less discount in the query would translate and would put a second definition of
// a bid's total in this file, which is the worse of the two mistakes.
//
// The integration-degraded banner reads this supplier's own award only. A failure on somebody else's is not this
// supplier's business and would leak that it exists.
//
// "Closing soon" has no documented window; seven days matches the buyer side, which is an invention but a
// consistent one. The same tender should not be urgent on one dashboard and not the other.

namespace MotsSupplierPortal.Infrastructure.Dashboards;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class SupplierDashboardHandler(AppDbContext db, IScopeContext scope) : ISupplierDashboardHandler
{
    private const int TopN = 5;

    private static readonly TimeSpan ClosingSoon = TimeSpan.FromDays(7);

    public async Task<SupplierDashboardDto?> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is not { } supplierId) return null;

        var supplier = await db.Suppliers.AsNoTracking().IncludeProfile().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null) return null;

        var now = DateTimeOffset.UtcNow;

        var invitations = db.Invitations.AsNoTracking().Where(i => i.SupplierId == supplierId);
        var proposals = db.Proposals.AsNoTracking().Where(p => p.SupplierId == supplierId);
        var documents = db.SupplierDocuments.AsNoTracking().Where(d => d.SupplierId == supplierId && d.IsLatestVersion);

        var kpis = new SupplierKpisDto(
            OpenInvitations: await invitations.CountAsync(
                i => i.Status != InvitationStatus.Declined && i.Status != InvitationStatus.Submitted, ct),
            DraftProposals: await proposals.CountAsync(p => p.State == ProposalState.Draft, ct),
            SubmittedProposals: await proposals.CountAsync(p => ProposalStates.InEvaluation.Contains(p.State), ct),
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

        var awardRows = await proposals
            .Where(p => ProposalStates.Resolved.Contains(p.State))
            .Select(p => new
            {
                p.ReferenceCode, p.State, p.Id, p.CurrencyCode,
                Rfq = db.Rfqs.Where(r => r.Id == p.RfqId)
                    .Select(r => new { r.ReferenceCode, r.TitleAr, r.TitleEn }).First(),
                DecidedAt = db.Awards.Where(a => a.WinningProposalId == p.Id)
                    .Select(a => a.AwardedAt).FirstOrDefault(),
            })
            .OrderBy(a => a.DecidedAt == null)
            .ThenByDescending(a => a.DecidedAt)
            .Take(TopN)
            .ToListAsync(ct);

        var awardedProposalIds = awardRows.Select(a => a.Id).ToList();
        var itemsByProposal = awardedProposalIds.Count == 0
            ? []
            : await db.ProposalItems.AsNoTracking()
                .Where(i => awardedProposalIds.Contains(i.ProposalId))
                .ToListAsync(ct);

        var missing = await DocumentCompletenessEvaluator.GetMissingRequiredDocumentTypeCodesAsync(db, supplierId, ct);

        var nextRequiredCode = missing.FirstOrDefault();
        var nextRequired = nextRequiredCode is null
            ? null
            : await db.Set<Domain.ReferenceData.DocumentType>().AsNoTracking()
                .Where(t => t.Code == nextRequiredCode)
                .Select(t => new { t.Code, t.NameAr, t.NameEn })
                .FirstOrDefaultAsync(ct);
        var requiredTotal =
            (await Suppliers.RequiredDocumentTypeResolver.ForSupplierAsync(db, supplierId, ct)).Count;
        var supplied = requiredTotal - missing.Count;

        return new SupplierDashboardDto(
            supplier.ReferenceCode, supplier.DisplayNameAr, supplier.DisplayNameEn,
            supplier.OnboardingState.ToString(), supplier.LifecycleState.ToString(),
            IsApproved: supplier.OnboardingState == SupplierOnboardingState.Approved,
            kpis, actionRequired,
            [.. invitationRows.Select(i => new DashboardInvitationDto(
                i.Rfq.ReferenceCode, i.Rfq.TitleAr, i.Rfq.TitleEn, i.Status.ToString(), i.Rfq.SubmissionClosesAt))],
            [.. proposalRows.Select(p => new DashboardProposalDto(
                p.ReferenceCode, p.Rfq.ReferenceCode, p.Rfq.TitleAr, p.Rfq.TitleEn, p.State.ToString(), p.ValidityEnd))],
            new ProfileHealthDto(
                Completeness: ProfileCompleteness.Ratio(
                    missingItems: supplier.GetMissingProfileFields().Count + missing.Count,
                    totalItems: Supplier.RequiredProfileFieldCodes.Count + requiredTotal),
                requiredTotal, supplied,
                NextRequiredDocumentTypeCode: nextRequired?.Code,
                NextRequiredDocumentNameAr: nextRequired?.NameAr,
                NextRequiredDocumentNameEn: nextRequired?.NameEn),
            ErpDegraded: await db.Awards.AsNoTracking().AnyAsync(
                a => a.ErpSyncStatus == ErpSyncStatus.Failed
                     && db.Proposals.Any(p => p.Id == a.WinningProposalId && p.SupplierId == supplierId), ct),
            Awards: [.. awardRows.Select(a =>
            {
                var lines = itemsByProposal.Where(i => i.ProposalId == a.Id).ToList();
                return new DashboardAwardDto(
                    a.Rfq.ReferenceCode, a.Rfq.TitleAr, a.Rfq.TitleEn,
                    a.ReferenceCode, a.State.ToString(), a.DecidedAt,
                    lines.Count == 0 ? null : lines.Sum(i => i.LineTotal),
                    a.CurrencyCode);
            })]);
    }
}

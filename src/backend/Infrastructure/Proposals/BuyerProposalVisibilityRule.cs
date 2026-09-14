// Which tier of a bid this buyer may see, for this tender, right now.
//
// One place, because the list and the detail must never disagree. A list that hides a total while the detail
// shows it is the same leak with an extra click.
//
// A tender that is not the caller's to see at all answers not-found rather than forbidden.
//
//
// THE THREE TIERS
//
// A supplier never reaches the buyer surface, even one who happens to hold the permission. That is the same
// first line the buyer-visible bid read draws.
//
// Sealed, while the submission window has not closed. Every state up to and including an open window is "not
// closed yet", and a cancelled tender has no bids to read.
//
// Technical, once the window has closed. Commercial, once the evaluation has been consolidated or finalised,
// which is the same pair of states the other buyer-side read treats as the seal being lifted, read here rather
// than re-derived.
//
// No evaluation at all counts as before consolidation, which is emphatically the point rather than a special
// case that skips it.
//
//
// WHAT COUNTS AS A BID AT ALL
//
// Submitted or later, never a draft. A draft is not a bid, and listing one would tell the buyer who is
// preparing to bid, which is the same disclosure the sealed tier exists to prevent arriving by another route.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class BuyerProposalVisibilityRule
{
    public static async Task<(Rfq Rfq, BuyerProposalVisibility Visibility)?> ResolveAsync(
        AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is not null) return null;

        var rfq = await db.Rfqs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;

        var windowClosed = rfq.State is not (RfqState.Draft or RfqState.InternalReview or RfqState.Approved
            or RfqState.Published or RfqState.SubmissionOpen or RfqState.Cancelled);
        if (!windowClosed) return (rfq, BuyerProposalVisibility.Sealed);

        var evaluationState = await db.Evaluations.AsNoTracking()
            .Where(e => e.RfqId == rfq.Id)
            .Select(e => (EvaluationState?)e.State)
            .FirstOrDefaultAsync(ct);

        var visibility = evaluationState is EvaluationState.Consolidated or EvaluationState.Finalized
            ? BuyerProposalVisibility.Commercial
            : BuyerProposalVisibility.Technical;

        return (rfq, visibility);
    }

    public static bool IsDisclosable(ProposalState state) =>
        state is not (ProposalState.Draft or ProposalState.Lapsed);
}

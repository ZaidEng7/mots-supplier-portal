using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// T-082's shared gate: which tier of a bid this caller may see, for this RFQ, right now.
///
/// <para>One place, because the list and the detail must never disagree — a list that hides a total
/// while the detail shows it is the same leak with an extra click.</para>
/// </summary>
internal static class BuyerProposalVisibilityRule
{
    /// <summary>
    /// Null when the RFQ is not the caller's to see at all (§9.2: a 404, not a 403).
    /// </summary>
    public static async Task<(Rfq Rfq, BuyerProposalVisibility Visibility)?> ResolveAsync(
        AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        // A supplier never reaches the buyer surface, even one who happens to hold the permission -
        // the same first line BuyerVisibleProposal draws.
        if (scope.SupplierId is not null) return null;

        var rfq = await db.Rfqs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;

        // Tier 1. Anything at or before SubmissionOpen keeps the bids sealed. Draft/InternalReview/
        // Approved/Published are all "the window has not closed", and Cancelled has no bids to read.
        var windowClosed = rfq.State is not (RfqState.Draft or RfqState.InternalReview or RfqState.Approved
            or RfqState.Published or RfqState.SubmissionOpen or RfqState.Cancelled);
        if (!windowClosed) return (rfq, BuyerProposalVisibility.Sealed);

        // Tier 3. The same two states BuyerVisibleProposal already treats as "the seal is lifted",
        // read here rather than re-derived: null covers "no evaluation opened", which is emphatically
        // before consolidation rather than a special case that skips it.
        var evaluationState = await db.Evaluations.AsNoTracking()
            .Where(e => e.RfqId == rfq.Id)
            .Select(e => (EvaluationState?)e.State)
            .FirstOrDefaultAsync(ct);

        var visibility = evaluationState is EvaluationState.Consolidated or EvaluationState.Finalized
            ? BuyerProposalVisibility.Commercial
            : BuyerProposalVisibility.Technical;

        return (rfq, visibility);
    }

    /// <summary>
    /// The bids a buyer may be shown at all: submitted or later, never a draft.
    ///
    /// <para>A draft is not a bid. Listing one would tell the buyer who is preparing to bid, which is
    /// the same disclosure the sealed tier exists to prevent, arriving by a different route.</para>
    /// </summary>
    public static bool IsDisclosable(ProposalState state) =>
        state is not (ProposalState.Draft or ProposalState.Lapsed);
}

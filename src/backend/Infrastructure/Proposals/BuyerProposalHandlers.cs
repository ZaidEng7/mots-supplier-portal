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

public sealed class ListBuyerProposalsHandler(AppDbContext db, IScopeContext scope) : IListBuyerProposalsHandler
{
    public async Task<BuyerProposalListDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        if (await BuyerProposalVisibilityRule.ResolveAsync(db, scope, rfqReferenceCode, ct) is not var (rfq, visibility))
        {
            return null;
        }

        var disclosable = await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfq.Id)
            .ToListAsync(ct);
        var bids = disclosable.Where(p => BuyerProposalVisibilityRule.IsDisclosable(p.State)).ToList();

        // The COUNT is disclosed even while sealed, and only the count. The workspace endpoint already
        // shows a submitted-proposal count to the same caller, so withholding it here would be a
        // narrower answer to a question already answered - while the identities stay sealed.
        if (visibility == BuyerProposalVisibility.Sealed)
        {
            return new BuyerProposalListDto(visibility, bids.Count, []);
        }

        var supplierIds = bids.Select(p => p.SupplierId).Distinct().ToList();
        var names = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayNameAr, s.DisplayNameEn })
            .ToDictionaryAsync(s => s.Id, s => (s.DisplayNameAr, s.DisplayNameEn), ct);

        var itemCounts = await db.ProposalItems.AsNoTracking()
            .Where(i => bids.Select(b => b.Id).Contains(i.ProposalId))
            .GroupBy(i => i.ProposalId)
            .Select(g => new { ProposalId = g.Key, Count = g.Count(), Total = g.Sum(x => (x.Quantity * x.UnitPrice) - (x.Discount ?? 0m)) })
            .ToDictionaryAsync(x => x.ProposalId, ct);

        var documentCounts = await db.ProposalDocuments.AsNoTracking()
            .Where(d => bids.Select(b => b.Id).Contains(d.ProposalId))
            .GroupBy(d => d.ProposalId)
            .Select(g => new { ProposalId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProposalId, x => x.Count, ct);

        var rows = bids
            .OrderBy(p => p.SubmittedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(p => p.ReferenceCode)
            .Select(p =>
            {
                var name = names.TryGetValue(p.SupplierId, out var n) ? n : ("", "");
                var totals = itemCounts.GetValueOrDefault(p.Id);
                return new BuyerProposalListItemDto(
                    p.Id, p.ReferenceCode, name.Item1, name.Item2, p.State, p.SubmittedAt,
                    totals?.Count ?? 0,
                    documentCounts.GetValueOrDefault(p.Id),
                    // Absent below the commercial tier, not zeroed - a zero reads as a free bid.
                    visibility == BuyerProposalVisibility.Commercial ? totals?.Total : null,
                    visibility == BuyerProposalVisibility.Commercial ? p.CurrencyCode : null);
            })
            .ToList();

        return new BuyerProposalListDto(visibility, bids.Count, rows);
    }
}

public sealed class GetBuyerProposalHandler(AppDbContext db, IScopeContext scope) : IGetBuyerProposalHandler
{
    public async Task<BuyerProposalDetailDto?> HandleAsync(string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        if (await BuyerProposalVisibilityRule.ResolveAsync(db, scope, rfqReferenceCode, ct) is not var (rfq, visibility))
        {
            return null;
        }

        // Sealed means sealed: a 404 on the detail, not an empty shell. An empty shell would confirm
        // the bid exists, which is the fact the tier is protecting.
        if (visibility == BuyerProposalVisibility.Sealed) return null;

        var proposal = await db.Proposals.AsNoTracking()
            .Include(p => p.Items)
            .Include(p => p.RequirementAnswers)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.RfqId == rfq.Id, ct);

        if (proposal is null || !BuyerProposalVisibilityRule.IsDisclosable(proposal.State)) return null;

        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == proposal.SupplierId)
            .Select(s => new { s.DisplayNameAr, s.DisplayNameEn })
            .FirstOrDefaultAsync(ct);

        var rfqItems = await db.RfqItems.AsNoTracking()
            .Where(i => i.RfqId == rfq.Id)
            .Select(i => new { i.Id, i.TitleAr, i.TitleEn })
            .ToDictionaryAsync(i => i.Id, ct);

        var requirements = await db.Requirements.AsNoTracking()
            .Where(r => r.RfqId == rfq.Id)
            .Select(r => new { r.Id, r.TextAr, r.TextEn })
            .ToDictionaryAsync(r => r.Id, ct);

        var commercial = visibility == BuyerProposalVisibility.Commercial;

        var items = proposal.Items
            .Select(i =>
            {
                var titles = rfqItems.GetValueOrDefault(i.RfqItemId);
                return new BuyerProposalItemDto(
                    i.RfqItemId, titles?.TitleAr ?? "", titles?.TitleEn ?? "", i.Quantity,
                    // Quantity and lead time are technical; price is not.
                    commercial ? i.UnitPrice : null,
                    commercial ? i.LineTotal : null,
                    i.LeadTimeDays, i.NotesAr, i.NotesEn);
            })
            .ToList();

        var answers = proposal.RequirementAnswers
            .Select(a =>
            {
                var text = requirements.GetValueOrDefault(a.RequirementId);
                return new BuyerProposalAnswerDto(a.RequirementId, text?.TextAr ?? "", text?.TextEn ?? "", a.AnswerAr, a.AnswerEn);
            })
            .ToList();

        return new BuyerProposalDetailDto(
            visibility, proposal.Id, proposal.ReferenceCode,
            supplier?.DisplayNameAr ?? "", supplier?.DisplayNameEn ?? "",
            proposal.State, proposal.SubmittedAt,
            proposal.NarrativeAr, proposal.NarrativeEn,
            answers, items, proposal.Documents.Count,
            commercial ? proposal.Items.Sum(i => i.LineTotal) : null,
            commercial ? proposal.CurrencyCode : null,
            commercial ? proposal.PaymentTerms : null,
            commercial ? proposal.IncotermCode : null);
    }
}

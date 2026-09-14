// The bids on one tender, as buyer staff may see them.
//
// What each row carries depends on the visibility tier the shared rule resolves.
//
// The COUNT is disclosed even while the bids are sealed, and only the count. The workspace already shows a
// submitted-bid count to the same caller, so withholding it here would be a narrower answer to a question
// already answered, while the identities stay sealed.
//
// Below the commercial tier the total and the currency are absent rather than zero. A zero reads as a free bid.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

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
                    visibility == BuyerProposalVisibility.Commercial ? totals?.Total : null,
                    visibility == BuyerProposalVisibility.Commercial ? p.CurrencyCode : null);
            })
            .ToList();

        return new BuyerProposalListDto(visibility, bids.Count, rows);
    }
}

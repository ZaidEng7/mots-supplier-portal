// Every bid belonging to the calling supplier, most recent activity first.
//
// Scoped by the caller's own company and never by a parameter. A company identifier in the request would be an
// authorisation decision made by the caller.
//
// Drafts ARE included, unlike the buyer's view of the same rows. A supplier's own unfinished bid is the thing
// they most need to find again; the buyer's list excludes drafts for the opposite reason. The two lists answer
// different questions about the same table.
//
// Submitted bids come first by recency and the drafts follow, because a supplier looking at this list is
// usually either chasing an outcome or finishing something.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListMyProposalsHandler(AppDbContext db, IScopeContext scope) : IListMyProposalsHandler
{
    public async Task<IReadOnlyList<MyProposalListItemDto>?> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is not { } supplierId) return null;

        var rows = await (
            from p in db.Proposals.AsNoTracking()
            join r in db.Rfqs.AsNoTracking() on p.RfqId equals r.Id
            where p.SupplierId == supplierId
            select new
            {
                p.Id,
                p.ReferenceCode,
                RfqCode = r.ReferenceCode,
                r.TitleAr,
                r.TitleEn,
                p.State,
                p.SubmittedAt,
                r.SubmissionClosesAt,
                p.CurrencyCode,
            }).ToListAsync(ct);

        var totals = await db.ProposalItems.AsNoTracking()
            .Where(i => rows.Select(x => x.Id).Contains(i.ProposalId))
            .GroupBy(i => i.ProposalId)
            .Select(g => new { ProposalId = g.Key, Count = g.Count(), Total = g.Sum(x => (x.Quantity * x.UnitPrice) - (x.Discount ?? 0m)) })
            .ToDictionaryAsync(x => x.ProposalId, ct);

        return
        [
            .. rows
                .OrderByDescending(x => x.SubmittedAt ?? DateTimeOffset.MinValue)
                .ThenBy(x => x.RfqCode)
                .Select(x => new MyProposalListItemDto(
                    x.ReferenceCode, x.RfqCode, x.TitleAr, x.TitleEn, x.State, x.SubmittedAt,
                    x.SubmissionClosesAt, x.CurrencyCode,
                    totals.GetValueOrDefault(x.Id)?.Total,
                    totals.GetValueOrDefault(x.Id)?.Count ?? 0))
        ];
    }
}

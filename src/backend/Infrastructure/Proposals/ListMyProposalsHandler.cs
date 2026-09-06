using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// SCR-150. Every proposal belonging to the calling supplier, newest activity first.
///
/// <para><b>Scoped by the caller's own SupplierId, never by a parameter.</b> A supplier id in the
/// request would be an authorization decision made by the client. The row-scope here is the same one
/// every other supplier-side read uses.</para>
///
/// <para>Drafts ARE included, unlike the buyer's view of the same rows: a supplier's own unfinished
/// bid is the thing they most need to find again. The buyer's list excludes them for the opposite
/// reason (T-082) — the two lists answer different questions about the same table.</para>
/// </summary>
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
                // Submitted first by recency, then the drafts - a supplier looking at this list is
                // usually either chasing an outcome or finishing something.
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

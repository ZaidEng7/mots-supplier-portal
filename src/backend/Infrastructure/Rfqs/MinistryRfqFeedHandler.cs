// Loads every invitation for the ministry's RFQ feed, with its proposal attached when one exists.
//
// ONE QUERY, NOT ONE PER ROW. The obvious shape - walk the invitations, then look up each supplier, tender and
// proposal - is 155 rows times three round trips today and grows with every tender published. The join is done
// in the database and the totals are summed there too, so the whole feed is one statement whatever its size.
//
// A LEFT JOIN ONTO THE PROPOSAL, because most invitations have none: 82 of 155 today. An inner join would have
// reported only the suppliers who replied, which is the opposite of what the ministry is measuring - they want
// to know how many were asked as well as how many answered.
//
// THE TOTAL IS SUMMED IN SQL from the proposal's lines, since a proposal stores no grand total in this product.
// Summing per row in memory would mean loading every line of every proposal to produce one number each.
//
// ORDERED BY TENDER THEN SUPPLIER so two runs a minute apart produce the same rows in the same order, which is
// what lets a nightly job diff one against the last.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class MinistryRfqFeedHandler(AppDbContext db) : IMinistryRfqFeedHandler
{
    public async IAsyncEnumerable<MinistryRfqFeedRecord> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var rows = from invitation in db.Invitations.AsNoTracking()
                   join rfq in db.Rfqs.AsNoTracking() on invitation.RfqId equals rfq.Id
                   join supplier in db.Suppliers.AsNoTracking() on invitation.SupplierId equals supplier.Id
                   join proposal in db.Proposals.AsNoTracking()
                       on new { invitation.RfqId, invitation.SupplierId } equals new { proposal.RfqId, proposal.SupplierId }
                       into proposals
                   from proposal in proposals.DefaultIfEmpty()
                   orderby rfq.ReferenceCode, supplier.ReferenceCode
                   select new MinistryRfqFeedRecord(
                       rfq.ReferenceCode,
                       supplier.ReferenceCode,
                       invitation.InvitedAt,
                       proposal == null ? null : proposal.ReferenceCode,
                       proposal == null ? null : proposal.State,
                       proposal == null ? null : proposal.CurrencyCode,
                       rfq.CurrencyCode,
                       proposal == null
                           ? null
                           : db.ProposalItems
                               .Where(i => i.ProposalId == proposal.Id)
                               .Sum(i => (decimal?)((i.Quantity * i.UnitPrice) - (i.Discount ?? 0m))));

        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return row;
        }
    }
}

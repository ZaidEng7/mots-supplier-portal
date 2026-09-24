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
//
// ONE PROPOSAL PER INVITATION, CHOSEN, AND THAT IS A FIX RATHER THAN A REFINEMENT
//
// This joined every proposal matching the tender and supplier, on the assumption that there is at most one. The
// database says otherwise: the uniqueness index on (RfqId, SupplierId) is FILTERED to exclude Withdrawn,
// Lapsed and Cancelled, so a supplier who withdraws a bid and submits another has two rows for one tender - and
// this feed emitted both. The ministry's sheet specifies one row per supplier per RFQ, so their loader was
// being handed a shape it does not describe, in the CSV as much as the JSON, since feed 4 shipped.
//
// Paging is what surfaced it: a cursor keyed on the pair cannot step past two rows that share it, so the walk
// returned one row fewer than the unpaged read and the difference was a duplicate rather than a missing row.
//
// The one that is reported is the supplier's live answer if they have one - a bid that has not been withdrawn,
// lapsed or cancelled - and otherwise their most recent ended bid. A tender where the only bid was withdrawn
// still reports "No Quote" against the withdrawn proposal rather than reporting nothing at all, which is the
// honest reading of what happened.
//
// ORDERED BY TENDER THEN SUPPLIER so two runs a minute apart produce the same rows in the same order, which is
// what lets a nightly job diff one against the last.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class MinistryRfqFeedHandler(AppDbContext db) : IMinistryRfqFeedHandler
{
    public async Task<IReadOnlyList<MinistryRfqFeedRecord>> PageAsync(
        string? afterRfqReferenceCode,
        string? afterSupplierReferenceCode,
        DateTimeOffset? modifiedSince,
        int limit,
        CancellationToken ct)
    {
        return await Rows(modifiedSince, afterRfqReferenceCode, afterSupplierReferenceCode)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async IAsyncEnumerable<MinistryRfqFeedRecord> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in Rows(modifiedSince: null).AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return row;
        }
    }

    // One query behind both representations. The stream and the page differ in how much they take, never in
    // what a row is or what order rows come in - a second copy of this join is a second place for the two to
    // disagree about which invitations exist.
    //
    // THE INCREMENTAL FILTER IS APPLIED TO THE COLUMNS AND NOT TO THE PROJECTED ROW, which reads as a detail
    // and is not one. Filtering on the record's own ModifiedAt is what anybody would write first, and the
    // database cannot do it: the projection is a constructor call, so the whole query collapses to client-side
    // evaluation - which in this case means loading every invitation in the country to discard most of them.
    // Expressed over the two timestamps, the comparison is a WHERE clause and the database answers it.
    private IQueryable<MinistryRfqFeedRecord> Rows(
        DateTimeOffset? modifiedSince, string? afterRfqReferenceCode = null, string? afterSupplierReferenceCode = null) =>
                   from invitation in db.Invitations.AsNoTracking()
                   join rfq in db.Rfqs.AsNoTracking() on invitation.RfqId equals rfq.Id
                   join supplier in db.Suppliers.AsNoTracking() on invitation.SupplierId equals supplier.Id
                   let proposal = db.Proposals.AsNoTracking()
                       .Where(p => p.RfqId == invitation.RfqId && p.SupplierId == invitation.SupplierId)
                       .OrderBy(p => p.State == ProposalState.Withdrawn
                           || p.State == ProposalState.Lapsed
                           || p.State == ProposalState.Cancelled ? 1 : 0)
                       .ThenByDescending(p => p.CreatedAt)
                       .FirstOrDefault()
                   where (modifiedSince == null
                           || (proposal != null && proposal.UpdatedAt > invitation.InvitedAt
                               ? proposal.UpdatedAt
                               : invitation.InvitedAt) > modifiedSince)
                       // The pair is ordered, so "after" means a later tender, or the same tender and a later
                       // supplier. Comparing only the tender would drop the rest of a tender a page ended
                       // inside. Like the incremental filter, this is expressed over the columns rather than
                       // over the projected row: EF cannot see inside a constructor call, so the record's own
                       // properties are not something the database can compare.
                       && (afterRfqReferenceCode == null || afterSupplierReferenceCode == null
                           || string.Compare(rfq.ReferenceCode, afterRfqReferenceCode) > 0
                           || (rfq.ReferenceCode == afterRfqReferenceCode
                               && string.Compare(supplier.ReferenceCode, afterSupplierReferenceCode) > 0))
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
                               .Sum(i => (decimal?)((i.Quantity * i.UnitPrice) - (i.Discount ?? 0m))),
                       proposal == null || proposal.UpdatedAt < invitation.InvitedAt
                           ? invitation.InvitedAt
                           : proposal.UpdatedAt);
}

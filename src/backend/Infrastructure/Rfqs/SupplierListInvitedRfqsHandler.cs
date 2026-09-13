// The tenders one supplier has been invited to.
//
// The invitation scope and the exclusion of tenders not yet published are applied before the cursor narrows
// the set, so they hold identically on every page rather than only on the first. A cross-organization test
// exists to prove exactly that.
//
// The total is a second query, off unless asked for, and counted before the cursor narrows anything.
//
//
// NOTHING HERE LOADS A CHILD COLLECTION
//
// Every field on a row is either a scalar on the tender or a correlated sub-select: the reader's own
// invitation status, the buying body's name, the count of requested lines, and whether the reader has a draft
// bid open.
//
// That is the point of the projection. A count of lines becomes a count in the database rather than a
// materialised list whose length is then read in memory, and the reader's own invitation is selected rather
// than the whole invitation collection being loaded and filtered afterwards.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierListInvitedRfqsHandler(AppDbContext db, IScopeContext scope) : ISupplierListInvitedRfqsHandler
{
    public async Task<ListEnvelope<SupplierRfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct)
    {
        var size = ListEnvelope<SupplierRfqListItemDto>.ClampPageSize(pageSize);
        if (scope.SupplierId is null) return ListEnvelope<SupplierRfqListItemDto>.Empty(size);

        var supplierId = scope.SupplierId.Value;

        var query = db.Rfqs
            .Where(r => r.Invitations.Any(i => i.SupplierId == supplierId)
                && r.State != RfqState.Draft && r.State != RfqState.InternalReview && r.State != RfqState.Approved);

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(r =>
                r.CreatedAt < from.At
                || (r.CreatedAt == from.At && r.Id.CompareTo(from.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                Dto = new SupplierRfqListItemDto(
                    r.ReferenceCode, r.TitleAr, r.TitleEn, r.State,
                    r.Invitations.Where(i => i.SupplierId == supplierId).Select(i => i.Status).FirstOrDefault(),
                    r.CreatedAt,
                    r.PublishedAt,
                    db.Organizations.Where(o => o.Id == r.OrganizationId)
                        .Select(o => new BuyingOrgDto(o.ReferenceCode, o.LegalNameEn)).FirstOrDefault(),
                    r.Items.Count(),
                    db.Proposals.Any(pr => pr.RfqId == r.Id
                        && pr.SupplierId == supplierId
                        && pr.State == ProposalState.Draft),
                    r.SubmissionClosesAt),
            })
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        var items = hasMore ? rows[..size] : rows;

        return ListEnvelope<SupplierRfqListItemDto>.Cursor(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new KeysetCursor(items[^1].Dto.CreatedAt, items[^1].Id).Encode() : null,
            size,
            totalCount,
            sort: "-createdAt");
    }
}

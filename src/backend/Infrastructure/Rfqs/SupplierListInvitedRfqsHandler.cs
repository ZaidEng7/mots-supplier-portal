using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

public sealed class SupplierListInvitedRfqsHandler(AppDbContext db, IScopeContext scope) : ISupplierListInvitedRfqsHandler
{
    public async Task<ListEnvelope<SupplierRfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, CancellationToken ct)
    {
        var size = ListEnvelope<SupplierRfqListItemDto>.ClampPageSize(pageSize);
        if (scope.SupplierId is null) return ListEnvelope<SupplierRfqListItemDto>.Empty(size);

        var supplierId = scope.SupplierId.Value;

        // The invitation-scoping predicate and the pre-Published exclusion are UNCHANGED, and are
        // applied before the cursor narrows the set - so they hold identically on every page, not
        // just the first. CrossOrganizationScopeTests' paging test exists to prove exactly that.
        var query = db.Rfqs
            .Where(r => r.Invitations.Any(i => i.SupplierId == supplierId)
                && r.State != RfqState.Draft && r.State != RfqState.InternalReview && r.State != RfqState.Approved);

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(r =>
                r.CreatedAt < from.At
                || (r.CreatedAt == from.At && r.Id.CompareTo(from.Id) < 0));
        }

        // MyInvitationStatus is resolved in SQL by a correlated subquery over this supplier's own
        // invitation row - the Invitations collection is never loaded, so the previous
        // `r.Invitations.Single(...)` in-memory filter is gone along with the include.
        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                // §12-A/D: §12.4's documented list fields. Every one is a correlated subquery or a
                // scalar on the row - NOTHING here loads a child collection. That is the whole point
                // of Batch 0.2's projection work: `r.Items.Count()` becomes a COUNT in SQL, not a
                // materialised Items list whose Count is then read in memory.
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
                    // T-054: a scalar on the row, so it costs nothing extra in this projection.
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

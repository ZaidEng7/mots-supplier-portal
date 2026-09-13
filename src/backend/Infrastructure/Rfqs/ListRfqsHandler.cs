// The buyer's list of their organization's tenders.
//
//
// WHAT THIS USED TO DO
//
// It loaded seven child collections for every tender in the organization, unpaged, plus a batched query for
// supplier names, in order to render three scalar columns.
//
// The child collections existed for the DETAIL read model; the list never touched them. It now selects the
// list's own shape in the database and pages by cursor, which the written contract names as the default for
// tenders.
//
// The organization scope is unchanged and is still the first condition applied. It is part of the same
// filter the cursor narrows, so it holds on page two exactly as on page one.
//
//
// THE OWNER FILTER
//
// The same three-value shape the review queue uses, for the same reasons. "Me" resolves on the server so the
// interface never needs to know its own user identifier. "Unassigned" surfaces the pool an officer would
// claim from, which is where every tender predating ownership lives. Anything else is one specific officer,
// for a manager looking at one person's load.
//
// The endpoint refuses an unrecognised value before reaching here.
//
//
// PAGING AND THE OWNER'S NAME
//
// Newest first with the identifier as tiebreak. One row beyond the page answers "is there more" without
// counting the whole filtered set. The total is a second query, off unless asked for, and counted before the
// cursor narrows anything.
//
// The owner's name comes from a correlated sub-select rather than a second round trip, because resolving
// names afterwards is exactly the per-row query the projection exists to avoid.
//
// It is absent when the tender is unowned, and also absent when the identifier points at a user row that is
// gone. Those are two different facts, and the screen tells them apart by whether the identifier is present.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class ListRfqsHandler(AppDbContext db, IScopeContext scope) : IListRfqsHandler
{
    public async Task<ListEnvelope<RfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, string? owner, CancellationToken ct)
    {
        var size = ListEnvelope<RfqListItemDto>.ClampPageSize(pageSize);
        if (scope.OrganizationId is null) return ListEnvelope<RfqListItemDto>.Empty(size);

        var query = db.Rfqs.Where(r => r.OrganizationId == scope.OrganizationId);

        if (owner == "me")
        {
            query = query.Where(r => r.OwnerUserId == scope.UserId);
        }
        else if (owner == "unassigned")
        {
            query = query.Where(r => r.OwnerUserId == null);
        }
        else if (owner is not null && Guid.TryParse(owner, out var ownerUserId))
        {
            query = query.Where(r => r.OwnerUserId == ownerUserId);
        }

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
                Dto = new RfqListItemDto(
                    r.ReferenceCode, r.TitleAr, r.TitleEn, r.State, r.CreatedAt,
                    r.OwnerUserId,
                    db.Users.Where(u => u.Id == r.OwnerUserId).Select(u => u.FullName).FirstOrDefault()),
            })
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        var items = hasMore ? rows[..size] : rows;

        return ListEnvelope<RfqListItemDto>.Cursor(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new KeysetCursor(items[^1].Dto.CreatedAt, items[^1].Id).Encode() : null,
            size,
            totalCount,
            sort: "-createdAt",
            filtersApplied: owner is null ? null : [$"owner={owner}"]);
    }
}

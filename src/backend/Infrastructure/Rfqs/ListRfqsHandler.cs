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

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>
/// T2 Item 2 + 3: projected and cursor-paginated.
///
/// <para>This previously ran <c>IncludeAll()</c> - seven child collections - plus a batched
/// supplier-name query, for every RFQ in the organization, unpaginated, to render three scalar
/// columns. The includes existed for <see cref="RfqDto"/>, the DETAIL shape; the list never touched
/// them. It now projects <see cref="RfqListItemDto"/> in SQL and pages by keyset per
/// API-ARCHITECTURE.md §6.1, which names RFQs a cursor-default collection.</para>
///
/// <para>Org scoping is unchanged and still the first predicate applied - it is part of the same
/// WHERE the cursor narrows, so it holds on page two exactly as on page one.</para>
/// </summary>
public sealed class ListRfqsHandler(AppDbContext db, IScopeContext scope) : IListRfqsHandler
{
    public async Task<ListEnvelope<RfqListItemDto>> HandleAsync(string? cursor, int? pageSize, bool withCount, string? owner, CancellationToken ct)
    {
        var size = ListEnvelope<RfqListItemDto>.ClampPageSize(pageSize);
        if (scope.OrganizationId is null) return ListEnvelope<RfqListItemDto>.Empty(size);

        var query = db.Rfqs.Where(r => r.OrganizationId == scope.OrganizationId);

        // A-7: the same three-value shape the review queue's ?assignedTo= already uses, and for the
        // same reason - "me" resolves server-side so the SPA never needs to know its own user id,
        // "unassigned" surfaces the pool an officer would claim from (which is where every RFQ that
        // predates ownership lives), and anything else is a specific officer, for a manager looking
        // at one person's load. The endpoint refuses an unrecognised value before reaching here.
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

        // pageSize + 1: the extra row answers HasMore without a COUNT over the whole filtered set.
        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            // The owner's NAME comes from a correlated sub-select rather than a second round trip:
            // this is a list path, and resolving N names afterwards is the N+1 the projection above
            // exists to avoid. Null when unowned, and null when the id points at a user row that is
            // gone - two different facts the screen distinguishes by the id being present or not.
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

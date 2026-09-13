using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// SCR-900's list, and the bell's.
///
/// <para><b>Row-scoped to the caller, in the query.</b> UX-WRITING §10: "Never leaks data across
/// scope (RBAC §6): suppliers see only their own". The WHERE clause is the enforcement - there is no
/// code path that reads notifications without it, because the recipient predicate is applied before
/// anything else and no parameter can widen it.</para>
///
/// <para>Cursor pagination per §6.1, which names notifications as a cursor-default collection. The
/// keyset is (CreatedAt desc, Id desc): newest first, with the GUIDv7 id as the tiebreaker so two
/// notifications written in the same transaction cannot straddle a page boundary.</para>
/// </summary>
public sealed class ListNotificationsHandler(AppDbContext db, IScopeContext scope) : IListNotificationsHandler
{
    public async Task<ListEnvelope<NotificationDto>> HandleAsync(string? cursor, int? pageSize, bool? unreadOnly, CancellationToken ct)
    {
        if (scope.UserId is not { } userId) return ListEnvelope<NotificationDto>.Empty(ListEnvelope<NotificationDto>.ClampPageSize(pageSize));

        var size = ListEnvelope<NotificationDto>.ClampPageSize(pageSize);

        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (unreadOnly == true) query = query.Where(n => n.ReadAt == null);

        if (NotificationCursor.TryDecode(cursor, out var createdAt, out var id))
        {
            query = query.Where(n => n.CreatedAt < createdAt || (n.CreatedAt == createdAt && n.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        var page = hasMore ? rows.Take(size).ToList() : rows;
        var next = hasMore && page.Count > 0 ? NotificationCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return ListEnvelope<NotificationDto>.Cursor(
            [.. page.Select(NotificationDtoMapper.ToDto)], hasMore, next, size,
            sort: "-createdAt",
            filtersApplied: unreadOnly == true ? ["unreadOnly"] : null);
    }
}

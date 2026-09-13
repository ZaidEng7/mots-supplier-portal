// The notification list, and the bell's.
//
// Scoped to the caller inside the query. The written rule is that nothing leaks across scope, and the filter IS
// the enforcement: no code path reads notifications without it, because the recipient test is applied before
// anything else and no parameter can widen it.
//
// Cursor-paged, which the contract names as the default for notifications. Newest first, with the
// time-ordered identifier as tiebreak so two notifications written in one transaction cannot straddle a page
// boundary.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

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

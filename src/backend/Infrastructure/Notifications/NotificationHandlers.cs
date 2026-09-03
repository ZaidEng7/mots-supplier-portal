using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.TitleAr, n.TitleEn, n.BodyAr, n.BodyEn, n.DataJson, n.CreatedAt, n.ReadAt, n.IsRead);
}

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

public sealed class UnreadNotificationCountHandler(AppDbContext db, IScopeContext scope) : IUnreadNotificationCountHandler
{
    public async Task<int> HandleAsync(CancellationToken ct) =>
        scope.UserId is not { } userId
            ? 0
            : await db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct);
}

/// <summary>
/// Marking read, scoped exactly as reading is.
///
/// <para>The scope predicate is part of the LOOKUP, not a check after it: a handler that loads by id
/// and then compares owners has already read the row, and every difference between "loaded then
/// refused" and "never found" is a signal. Here the two are the same query.</para>
/// </summary>
public sealed class MarkNotificationReadHandler(AppDbContext db, IScopeContext scope) : IMarkNotificationReadHandler
{
    public async Task<MarkNotificationReadResult> HandleAsync(Guid notificationId, CancellationToken ct)
    {
        if (scope.UserId is not { } userId) return new MarkNotificationReadResult.NotFoundOrOutOfScope();

        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, ct);

        if (notification is null) return new MarkNotificationReadResult.NotFoundOrOutOfScope();

        notification.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return new MarkNotificationReadResult.Success(NotificationDtoMapper.ToDto(notification));
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        if (scope.UserId is not { } userId) return 0;

        var now = DateTimeOffset.UtcNow;
        return await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, now), ct);
    }
}

/// <summary>
/// The keyset cursor. Opaque to the client on purpose - a cursor that reads as a timestamp invites
/// callers to construct one, and then its format is a contract.
/// </summary>
internal static class NotificationCursor
{
    public static string Encode(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}|{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');

            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out id)) return false;

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

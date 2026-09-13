// Marking one notification read, and marking them all read.
//
// The scope test is part of the LOOKUP rather than a check after it. A handler that loads by identifier and then
// compares owners has already read the row, and every difference between "loaded then refused" and "never found"
// is a signal. Here the two are the same query.
//
// Marking everything read is a single conditional update rather than a load and a loop, because the count can be
// large and none of the rows are needed afterwards.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

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

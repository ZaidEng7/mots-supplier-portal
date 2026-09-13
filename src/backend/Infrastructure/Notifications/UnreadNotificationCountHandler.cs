// The unread count behind the bell, for the caller only.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class UnreadNotificationCountHandler(AppDbContext db, IScopeContext scope) : IUnreadNotificationCountHandler
{
    public async Task<int> HandleAsync(CancellationToken ct) =>
        scope.UserId is not { } userId
            ? 0
            : await db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct);
}

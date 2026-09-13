// Reading a user's notification preferences: every type, classified, with their own choices applied.
//
// Ordered by type name so the screen's grouping is stable between loads.
//
// The classification decides what may be switched off. The stored rows decide only what currently is.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetNotificationPreferencesHandler(
    AppDbContext db, IScopeContext scope, INotificationCopySource copy)
    : IGetNotificationPreferencesHandler
{
    public async Task<NotificationPreferencesDto> HandleAsync(CancellationToken ct)
    {
        var muted = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == scope.UserId)
            .Select(p => p.NotificationType)
            .ToListAsync(ct);
        var mutedSet = muted.ToHashSet(StringComparer.Ordinal);

        return new NotificationPreferencesDto(
            await NotificationPreferenceDescriptions.DescribeAsync(copy, type => NotificationClassification.IsMuteable(type) && mutedSet.Contains(type), ct));
    }
}

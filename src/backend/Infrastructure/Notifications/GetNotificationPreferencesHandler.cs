using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// SCR-901's read: all 32 types, classified per D-60, with this user's own choices applied.
/// </summary>
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

        // Ordered by type name so the screen's grouping is stable between loads. The classification decides
        // muteability; the stored rows decide only what is currently off.
        return new NotificationPreferencesDto(
            await NotificationPreferenceDescriptions.DescribeAsync(copy, type => NotificationClassification.IsMuteable(type) && mutedSet.Contains(type), ct));
    }
}

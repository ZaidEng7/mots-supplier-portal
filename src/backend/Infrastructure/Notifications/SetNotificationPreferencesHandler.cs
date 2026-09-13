// A user switches notification types off, and the place the classification is enforced.
//
// The classification is checked here and not trusted from the caller. The screen renders the types that cannot be
// muted as always on, but a caller that sends one anyway must be refused: this endpoint is where "a user may not
// switch off an award outcome" becomes true, and a screen is not a boundary.
//
// Unknown types are refused by name too, so a stale client learns which values it sent are no longer types.
//
//
// REPLACE, NOT MERGE
//
// The command carries the whole muted set and the stored rows are made to match it exactly.
//
// That is what makes sending the same set twice a no-op, and what lets a user UNMUTE something by leaving it out.
//
// The authenticated user is asserted rather than assumed, because a missing one here would write preferences
// against an empty identifier and mute them for nobody.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SetNotificationPreferencesHandler(
    AppDbContext db, IScopeContext scope, INotificationCopySource copy)
    : ISetNotificationPreferencesHandler
{
    public async Task<SetNotificationPreferencesResult> HandleAsync(
        SetNotificationPreferencesCommand command, CancellationToken ct)
    {
        var requested = command.MutedTypes.Distinct(StringComparer.Ordinal).ToList();

        var unknown = requested.Where(type => !NotificationTypes.All.Contains(type)).ToList();
        if (unknown.Count > 0) return new SetNotificationPreferencesResult.UnknownTypes(unknown);

        var notMuteable = requested.Where(type => !NotificationClassification.IsMuteable(type)).ToList();
        if (notMuteable.Count > 0) return new SetNotificationPreferencesResult.NotMuteable(notMuteable);

        var userId = scope.UserId ?? throw new InvalidOperationException(
            "Notification preferences require an authenticated user.");
        var existing = await db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var requestedSet = requested.ToHashSet(StringComparer.Ordinal);

        db.NotificationPreferences.RemoveRange(
            existing.Where(p => !requestedSet.Contains(p.NotificationType)));

        var already = existing.Select(p => p.NotificationType).ToHashSet(StringComparer.Ordinal);
        foreach (var type in requested.Where(type => !already.Contains(type)))
        {
            db.NotificationPreferences.Add(new NotificationPreference
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                NotificationType = type,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        return new SetNotificationPreferencesResult.Success(new NotificationPreferencesDto(
            await NotificationPreferenceDescriptions.DescribeAsync(copy, requestedSet.Contains, ct)));
    }
}

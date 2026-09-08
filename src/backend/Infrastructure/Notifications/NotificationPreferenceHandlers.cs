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

/// <summary>
/// SCR-901's write, and the place D-60 is enforced.
///
/// <para><b>The classification is checked here, not trusted from the client.</b> The screen renders actionable
/// types as always-on, but a caller that sends one anyway must be refused: this endpoint is the boundary
/// where "a user may not switch off an award outcome" becomes true, and a screen is not a boundary.</para>
///
/// <para><b>Replace, not merge.</b> The command carries the whole muted set, so the stored rows are made to
/// match it exactly - which is what makes sending the same set twice a no-op, and what lets a user UNMUTE
/// something by leaving it out.</para>
/// </summary>
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

        // The route requires an authenticated caller, so the scope's user is present - asserted rather than
        // assumed, because a null here would write preferences against Guid.Empty and mute them for nobody.
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

/// <summary>
/// Every type, with its classification and the words the user would recognise.
///
/// <para>Shared by the read and the write so the two responses cannot disagree - the write returns the new
/// state, and a screen that re-rendered from a differently-shaped response would flicker or, worse, show a
/// stale muteability.</para>
/// </summary>
internal static class NotificationPreferenceDescriptions
{
    public static async Task<IReadOnlyList<NotificationPreferenceDto>> DescribeAsync(
        INotificationCopySource copy, Func<string, bool> isMuted, CancellationToken ct)
    {
        var described = new List<NotificationPreferenceDto>(NotificationTypes.All.Count);

        foreach (var type in NotificationTypes.All.OrderBy(type => type, StringComparer.Ordinal))
        {
            // One lookup per type, on one screen. The copy source reads the shipped catalogue plus any
            // administrator override, which is exactly the pair a user should see named here.
            var entry = await copy.ForAsync(type, ct);
            var muteable = NotificationClassification.IsMuteable(type);

            described.Add(new NotificationPreferenceDto(
                type, muteable, muteable && isMuted(type), entry.TitleAr, entry.TitleEn));
        }

        return described;
    }
}

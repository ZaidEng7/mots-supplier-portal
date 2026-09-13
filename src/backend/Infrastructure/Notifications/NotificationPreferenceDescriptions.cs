using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

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

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.TitleAr, n.TitleEn, n.BodyAr, n.BodyEn, n.DataJson, n.CreatedAt, n.ReadAt, n.IsRead,
            // T-037. The same classification that decides what a person may mute decides which group
            // they read it in - one set, in the domain, rather than a copy of it in the SPA.
            NotificationClassification.IsActionable(n.Type));
}

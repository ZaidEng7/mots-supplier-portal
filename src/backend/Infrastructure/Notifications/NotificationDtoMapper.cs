// Turning a notification row into its read model.
//
// Whether it is actionable comes from the same classification that decides what a person may mute, so one set in
// the domain decides which group they read it in rather than a copy of that set in the interface.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.TitleAr, n.TitleEn, n.BodyAr, n.BodyEn, n.DataJson, n.CreatedAt, n.ReadAt, n.IsRead,
            NotificationClassification.IsActionable(n.Type));
}

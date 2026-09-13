// The vocabulary for one notification as the bell and the notification centre show it.
//
// Both languages are returned rather than the caller's one. Delivery is meant to be bilingual per the
// reader's language, and the language is a client concern: the interface switches without asking the server
// again, so a server-picked string would be stale the moment somebody toggles.
//
//
// WHY THE ACTIONABLE FLAG IS ON THE WIRE
//
// It says which of the two groups the row belongs to: the things the reader must do something about, and the
// things that merely happened.
//
// The classification decides which notifications a person may switch off, and it lives in one set in the
// domain. A second copy in the interface would be a second answer to whether something is actionable, and the
// two would disagree the first time a notification type is added, which is exactly how a reader ends up
// unable to find a message they are still receiving.

namespace MotsSupplierPortal.Application.Notifications;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string Data,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    bool IsRead,
    bool IsActionable);

public interface IListNotificationsHandler
{
    Task<ListEnvelope<NotificationDto>> HandleAsync(string? cursor, int? pageSize, bool? unreadOnly, CancellationToken ct);
}

public interface IUnreadNotificationCountHandler
{
    Task<int> HandleAsync(CancellationToken ct);
}

public abstract record MarkNotificationReadResult
{
    public sealed record Success(NotificationDto Notification) : MarkNotificationReadResult;

    public sealed record NotFoundOrOutOfScope : MarkNotificationReadResult;
}

public interface IMarkNotificationReadHandler
{
    Task<MarkNotificationReadResult> HandleAsync(Guid notificationId, CancellationToken ct);
    Task<int> MarkAllReadAsync(CancellationToken ct);
}

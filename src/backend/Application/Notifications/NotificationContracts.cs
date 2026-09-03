using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Notifications;

namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// One row of SCR-900's list and of the bell panel.
///
/// <para>Both languages are returned, not the caller's one. UX-WRITING §10 requires delivery
/// "bilingual per the user's locale", and the locale is a client concern - the SPA switches language
/// without a round-trip, so a server-picked string would be stale the moment someone toggles.</para>
/// </summary>
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
    bool IsRead);

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

    /// <summary>
    /// §9.2: an out-of-scope notification and an unknown id are the SAME answer. A notification
    /// belonging to someone else must not be distinguishable from one that never existed - the id is
    /// the only thing an attacker would be probing with.
    /// </summary>
    public sealed record NotFoundOrOutOfScope : MarkNotificationReadResult;
}

public interface IMarkNotificationReadHandler
{
    Task<MarkNotificationReadResult> HandleAsync(Guid notificationId, CancellationToken ct);
    Task<int> MarkAllReadAsync(CancellationToken ct);
}

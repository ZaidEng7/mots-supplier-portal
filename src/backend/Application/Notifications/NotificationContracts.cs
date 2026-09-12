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
    bool IsRead,
    /// <summary>
    /// T-037/INFORMATION-ARCHITECTURE §2: which of the two groups this row belongs to - the things
    /// the reader must do something about, and the things that merely happened.
    ///
    /// <para><b>Why it is on the wire rather than worked out by the client.</b> The classification is
    /// D-60's, it decides which notifications a person may switch off, and it lives in one set in the
    /// domain. A second copy in the SPA would be a second answer to "is this actionable", and the two
    /// would disagree the first time a notification type is added - which is exactly how a reader
    /// ends up with a muted row in the column that says you must act.</para>
    ///
    /// <para>The bell itself refused to group for want of this flag, and said so in a comment: nothing
    /// classified the types when it was written. D-60 then classified all of them for a different
    /// reason, and the refusal outlived its reason.</para>
    /// </summary>
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

namespace MotsSupplierPortal.Domain.Notifications;

/// <summary>
/// SCR-901/FR-NOT-004: one notification type this user has switched off.
///
/// <para><b>Muted types are stored, not preferences.</b> A row means "do not deliver this type to this
/// user"; no row means deliver. The alternative - a row per user per type carrying a boolean - would need
/// backfilling for every existing user the day a thirty-third notification type is added, and a user with no
/// rows yet would be indistinguishable from one who had muted everything. Absence meaning DELIVER is also
/// the safe direction: a bug that loses these rows over-delivers, which irritates somebody, rather than
/// silently suppressing an award notice.</para>
///
/// <para><b>Only informational types may appear here</b> (D-60). That is enforced where the write happens
/// rather than by the schema, because <c>NotificationClassification</c> is the record of the decision and a
/// check constraint listing type names would be a second copy of it - one that a migration would have to
/// rewrite every time the classification changed.</para>
/// </summary>
public sealed class NotificationPreference
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    /// <summary>A <see cref="NotificationTypes"/> constant. Text rather than an enum for the same reason
    /// the notification's own type column is: the value is persisted and read by the copy catalogue, so
    /// renaming one must be a visible change to a string that already exists in rows.</summary>
    public required string NotificationType { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

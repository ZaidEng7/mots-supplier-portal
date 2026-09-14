// One notification type this user has switched off.
//
// What is stored is the mute, not the preference. A row means "do not deliver this type to this
// user"; no row means deliver.
//
// The alternative, a row per user per type carrying a yes-or-no, would need back-filling for every
// existing user the day a thirty-third notification type is added, and a user with no rows yet
// would look identical to one who had muted everything. Absence meaning deliver is also the safe
// direction: a bug that loses these rows over-delivers and annoys somebody, rather than silently
// swallowing an award notice.
//
// Only informational types may appear here. That is checked where the write happens rather than in
// the database, because the classification list is the record of the decision, and a constraint
// listing type names would be a second copy of it that every change to the classification would
// have to rewrite.
//
// NotificationType is text rather than an enum, for the same reason the notification's own type
// column is: the value is stored in rows and read by the wording catalogue, so renaming one has to
// be a visible change to a string that already exists in the database.

namespace MotsSupplierPortal.Domain.Notifications;

public sealed class NotificationPreference
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public required string NotificationType { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

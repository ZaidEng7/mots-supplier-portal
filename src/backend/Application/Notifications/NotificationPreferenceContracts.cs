// The vocabulary for the preferences screen: one row per notification type.
//
// The type is the same constant the user's stored preference keys on.
//
// Muteable is false for the actionable types, and the screen renders those as permanently on rather than
// hiding them. A user who cannot find a message they are still receiving would otherwise conclude the screen
// is broken, and the four protected families are exactly the ones somebody would look for first.
//
// Muted is whether this user has switched it off, and is always false for a type that cannot be muted.
//
// The title comes from the wording catalogue, including an administrator's rewording where there is one. It is
// served rather than duplicated as thirty-two more interface strings: the words a user recognises are the
// words they were sent, and a second copy in the interface would drift from the first the moment somebody
// reworded a template.

namespace MotsSupplierPortal.Application.Notifications;

public sealed record NotificationPreferenceDto(
    string Type, bool Muteable, bool Muted, string TitleAr, string TitleEn);

public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationPreferenceDto> Types);

public sealed record SetNotificationPreferencesCommand(IReadOnlyList<string> MutedTypes);

public abstract record SetNotificationPreferencesResult
{
    public sealed record Success(NotificationPreferencesDto Preferences) : SetNotificationPreferencesResult;

    public sealed record UnknownTypes(IReadOnlyList<string> Types) : SetNotificationPreferencesResult;

    public sealed record NotMuteable(IReadOnlyList<string> Types) : SetNotificationPreferencesResult;
}

public interface IGetNotificationPreferencesHandler
{
    Task<NotificationPreferencesDto> HandleAsync(CancellationToken ct);
}

public interface ISetNotificationPreferencesHandler
{
    Task<SetNotificationPreferencesResult> HandleAsync(SetNotificationPreferencesCommand command, CancellationToken ct);
}

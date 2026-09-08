namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// SCR-901: one notification type, as the preferences screen shows it.
/// </summary>
/// <param name="Type">The <c>NotificationTypes</c> constant, which is also what the user's stored preference
/// keys on.</param>
/// <param name="Muteable">D-60: false for the actionable types, and the screen renders those as always-on
/// rather than hiding them. A user who cannot find a message they are receiving would otherwise conclude the
/// preferences screen is broken - and the four families D-60 protects are exactly the ones somebody would
/// look for first.</param>
/// <param name="Muted">Whether this user has switched it off. Always false for an unmuteable type.</param>
/// <param name="TitleAr">The notification's own title from the copy catalogue, including an administrator's
/// SCR-717 rewording if there is one. Served rather than duplicated as 32 more interface strings: the words a
/// user recognises are the words they were sent, and a second copy in the SPA would drift from the first the
/// moment somebody reworded a template.</param>
public sealed record NotificationPreferenceDto(
    string Type, bool Muteable, bool Muted, string TitleAr, string TitleEn);

/// <summary>
/// SCR-901's whole response: every type this system can produce, classified and with the caller's own
/// choices applied.
///
/// <para><b>All 32, not just the muteable ones.</b> The screen's job is partly to say what a user is going
/// to be told regardless - which is D-60's substance, and unrenderable from a list that omits it.</para>
/// </summary>
public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationPreferenceDto> Types);

/// <summary>
/// The whole muted set, replacing whatever was stored.
///
/// <para><b>A set, not a toggle per type.</b> What a user decides on this screen is "these are the ones I do
/// not want", one decision - and a per-type toggle endpoint would make the screen's state the sum of N
/// requests, any of which can fail on its own. Same reasoning as BRULE-016's category links.</para>
/// </summary>
public sealed record SetNotificationPreferencesCommand(IReadOnlyList<string> MutedTypes);

public abstract record SetNotificationPreferencesResult
{
    public sealed record Success(NotificationPreferencesDto Preferences) : SetNotificationPreferencesResult;

    /// <summary>A type this system does not produce. Refused rather than stored, because a stored row for a
    /// type nobody sends is a preference that can never be honoured and never be seen to fail.</summary>
    public sealed record UnknownTypes(IReadOnlyList<string> Types) : SetNotificationPreferencesResult;

    /// <summary>D-60's constraint, enforced: invitations, clarification requests, award outcomes and the
    /// actionable remainder cannot be switched off. Refused with the offending types named, rather than
    /// silently dropped from the set - a screen that appeared to accept a mute it did not apply would be
    /// worse than one that refuses.</summary>
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

namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// FR-ADM-007/T-061/SCR-715: the admin surface for notification copy.
///
/// <para>Each item carries the shipped words alongside the current ones, so the screen can show what
/// an override replaced and offer a revert that is not guesswork. It also carries the tokens the type
/// permits - the screen must be able to say "{rfqCode} is available here" without a second copy of
/// the catalogue.</para>
/// </summary>
public sealed record NotificationTemplateDto(
    string Type,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    /// <summary>The shipped catalogue's words for this type. Present whether or not it is overridden:
    /// an administrator deciding whether to revert needs to see what they would revert to.</summary>
    string ShippedTitleAr,
    string ShippedTitleEn,
    string ShippedBodyAr,
    string ShippedBodyEn,
    bool IsOverridden,
    DateTimeOffset? UpdatedAt,
    /// <summary>Tokens this type's payload can fill. A template may use any subset and no others.</summary>
    string[] AvailableTokens);

public sealed record UpdateNotificationTemplateCommand(
    string Type, string TitleAr, string TitleEn, string BodyAr, string BodyEn);

public abstract record NotificationTemplateResult
{
    public sealed record Success(NotificationTemplateDto Template) : NotificationTemplateResult;

    /// <summary>Not a notification type this system emits.</summary>
    public sealed record UnknownType : NotificationTemplateResult;

    /// <summary>
    /// The template names a token the type cannot fill. <paramref name="Tokens"/> lists the offending
    /// ones so the message can name them.
    ///
    /// <para>Refused rather than accepted-and-left-literal: an unfillable token reaches the supplier
    /// as the characters <c>{price}</c> in the middle of a sentence, which looks like a broken portal
    /// and cannot be diagnosed from the notification row.</para>
    /// </summary>
    public sealed record UnknownTokens(string[] Tokens) : NotificationTemplateResult;
}

public interface INotificationTemplateAdminHandler
{
    Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(CancellationToken ct);

    Task<NotificationTemplateResult> UpdateAsync(UpdateNotificationTemplateCommand command, CancellationToken ct);

    /// <summary>Removes the override, restoring the shipped copy. Returns the shipped state, or
    /// UnknownType. Removing an override that does not exist is a success: the outcome the caller
    /// asked for is already true.</summary>
    Task<NotificationTemplateResult> RevertAsync(string type, CancellationToken ct);
}

/// <summary>What the notification writer reads: the override if there is one, otherwise the shipped
/// entry. Kept separate from the admin handler so the write path depends on nothing it does not
/// need.</summary>
public interface INotificationCopySource
{
    Task<NotificationCatalogue.Entry> ForAsync(string type, CancellationToken ct);
}

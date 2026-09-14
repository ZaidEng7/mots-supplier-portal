// The vocabulary for the screen where an administrator rewords notifications.
//
// Each row carries the shipped words alongside the current ones, so the screen can show what an override
// replaced and offer a revert that is not guesswork. The shipped words are present whether or not the type is
// overridden, because an administrator deciding whether to revert needs to see what they would revert to.
//
// It also carries the placeholders that type's payload can fill, so the screen can say which are available
// here without keeping a second copy of the catalogue. A wording may use any subset of them and no others.
//
// A type this system does not emit is its own refusal, separate from a wording that is invalid.

namespace MotsSupplierPortal.Application.Notifications;

public sealed record NotificationTemplateDto(
    string Type,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string ShippedTitleAr,
    string ShippedTitleEn,
    string ShippedBodyAr,
    string ShippedBodyEn,
    bool IsOverridden,
    DateTimeOffset? UpdatedAt,
    string[] AvailableTokens);

public sealed record UpdateNotificationTemplateCommand(
    string Type, string TitleAr, string TitleEn, string BodyAr, string BodyEn);

public abstract record NotificationTemplateResult
{
    public sealed record Success(NotificationTemplateDto Template) : NotificationTemplateResult;

    public sealed record UnknownType : NotificationTemplateResult;

    public sealed record UnknownTokens(string[] Tokens) : NotificationTemplateResult;
}

public interface INotificationTemplateAdminHandler
{
    Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(CancellationToken ct);

    Task<NotificationTemplateResult> UpdateAsync(UpdateNotificationTemplateCommand command, CancellationToken ct);

    Task<NotificationTemplateResult> RevertAsync(string type, CancellationToken ct);
}

public interface INotificationCopySource
{
    Task<NotificationCatalogue.Entry> ForAsync(string type, CancellationToken ct);
}

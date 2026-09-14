// The vocabulary for the account screen: what a signed-in person may see and change about themselves.
//
// No roles and no permissions. The interface reads those out of the access token's own claims, and a second
// copy here would be a second source of truth for authorisation data, one that disagrees with the token the
// moment a role changes mid-session. This carries only what the screen edits or displays.
//
// The email address is shown and not editable. Changing it means verifying the new one, and no document here
// defines that flow; the screen's own specification lists name, language and contact, and not email. It is
// shown so a person can see where their notifications go, and the alternative to leaving it read-only is
// inventing an identity-change lifecycle.
//
// The language-chosen flag is false until the person has picked a language for themselves, which is how the
// first-run chooser knows to ask once.

namespace MotsSupplierPortal.Application.Auth;

public sealed record AccountDto(string FullName, string Email, string Language, bool LanguageChosen);

public sealed record UpdateAccountCommand(Guid UserId, string FullName, string Language);

public sealed record ChooseLanguageCommand(Guid UserId, string Language);

public interface IGetAccountHandler
{
    Task<AccountDto?> HandleAsync(Guid userId, CancellationToken ct);
}

public interface IUpdateAccountHandler
{
    Task<AccountDto?> HandleAsync(UpdateAccountCommand command, CancellationToken ct);
}

public interface IChooseLanguageHandler
{
    Task<AccountDto?> HandleAsync(ChooseLanguageCommand command, CancellationToken ct);
}

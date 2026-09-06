namespace MotsSupplierPortal.Application.Auth;

/// <summary>
/// SCR-902's read. The account facts a signed-in user owns about themselves.
///
/// <para><b>No roles and no permissions.</b> DECISIONS-TAKEN.md D-26: the SPA decodes those out of
/// the access token's own claims, and a second copy here would be a second source of truth for
/// authorization data - one that disagrees with the token the moment a role changes mid-session.
/// This DTO carries only what the account screen edits or displays.</para>
///
/// <para><b>Email is read-only.</b> Changing it means re-verifying it, and no document in this
/// repository defines that flow - SCREEN-INVENTORY.md's own row for SCR-902 lists "Name, language,
/// numerals, contact" and not email. Shown so the user can see which address their notifications go
/// to; not editable, because the alternative is inventing an identity-change lifecycle.</para>
/// </summary>
public sealed record AccountDto(string FullName, string Email, string Language);

/// <summary>
/// SCR-902's write. Both fields are the user's own, so there is no id in the command: the identity
/// comes from the session, and a user id in this payload would let any authenticated caller rename
/// anyone.
/// </summary>
public sealed record UpdateAccountCommand(Guid UserId, string FullName, string Language);

public interface IGetAccountHandler
{
    Task<AccountDto?> HandleAsync(Guid userId, CancellationToken ct);
}

public interface IUpdateAccountHandler
{
    Task<AccountDto?> HandleAsync(UpdateAccountCommand command, CancellationToken ct);
}

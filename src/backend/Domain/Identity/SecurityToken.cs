// A single-use token sent by email: verify your address, reset your password, accept an invitation.
//
// Only the hash is stored, and a token is spent the first time it is used.
//
// The framework has its own token providers, and they are deliberately not used for this. Those
// tokens carry enough inside themselves to be checked without a stored record, but the links sent
// to people must contain only the opaque token and never the user's id, which means the token itself
// has to be the only way to find the user. The framework's providers are still used afterwards, to
// perform the actual change once this token has identified who is asking.
//
// Purpose is stored as text, so adding a new purpose needs no database migration. Only changing the
// column's type would.
//
// A token is valid while it has not been used and has not expired.

namespace MotsSupplierPortal.Domain.Identity;

public enum SecurityTokenPurpose
{
    EmailVerification,
    PasswordReset,
    SupplierUserInvite,
    StaffInvite,
}

public sealed class SecurityToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public SecurityTokenPurpose Purpose { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public bool IsValid => ConsumedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

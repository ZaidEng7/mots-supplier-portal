namespace MotsSupplierPortal.Domain.Identity;

public enum SecurityTokenPurpose
{
    EmailVerification,
    PasswordReset,
    SupplierUserInvite,
    // Task #28. Stored as a string column (see migrations), so adding this value needs no
    // migration - only a schema change to the column TYPE would.
    StaffInvite,
}

/// <summary>
/// Opaque, single-use, hashed tokens for email verification and password reset
/// (SECURITY-ARCHITECTURE.md §1.6/§1.7). Deliberately NOT ASP.NET Identity's built-in
/// DataProtector token providers - those encode enough into the token's validation to be
/// checked without a server-side record, but the URLs the client receives must contain only the
/// opaque token (never the user id), so the token itself has to be the sole lookup key. Identity's
/// own token providers are still used internally (see SecurityTokenService) to perform the actual
/// state change once this token has resolved a user.
/// </summary>
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

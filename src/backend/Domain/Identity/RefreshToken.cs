// One refresh token from a signed-in session, stored as a hash.
//
// Tokens rotate: each use issues a new one and retires the old. Every token from one sign-in shares
// a FamilyId, and using a token that has already been rotated out revokes the whole family. That is
// what turns a stolen token into a dead session rather than a silent second user.
//
// Only the hash is stored, so the database never holds a token that could be replayed.
//
// A token is active while it has not been revoked and has not expired.

namespace MotsSupplierPortal.Domain.Identity;

public sealed class RefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public Guid FamilyId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

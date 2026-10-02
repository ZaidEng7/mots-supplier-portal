// One refresh token from a signed-in session, stored as a hash.
//
// Tokens rotate: each use issues a new one and retires the old. Every token from one sign-in shares
// a FamilyId, and using a token that has already been rotated out revokes the whole family. That is
// what turns a stolen token into a dead session rather than a silent second user.
//
// Only the hash is stored, so the database never holds a token that could be replayed.
//
// A token is active while it has not been revoked and has not expired. The rule is written once, as an
// expression the database can run, and IsActive is that same expression compiled for a token already in
// memory. A person's session list and the active-session count on the staff surfaces both filter on it, so the
// count and the list cannot disagree. Inside a query the clock is the database's rather than this process's.

namespace MotsSupplierPortal.Domain.Identity;

using System.Linq.Expressions;

public sealed class RefreshToken
{
    public static readonly Expression<Func<RefreshToken, bool>> Active =
        t => t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow;

    private static readonly Func<RefreshToken, bool> IsActiveNow = Active.Compile();

    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public Guid FamilyId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }

    public bool IsActive => IsActiveNow(this);
}

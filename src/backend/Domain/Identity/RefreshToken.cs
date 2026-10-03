// One refresh token from a signed-in session, stored as a hash.
//
// Tokens rotate: each use issues a new one and retires the old. Every token from one sign-in shares
// a FamilyId, and using a token that was rotated out more than ten seconds earlier revokes the whole
// family. That is what turns a stolen token into a dead session rather than a silent second user.
//
// Inside those ten seconds a rotated token is refused and nothing else happens, because that is a second
// request from the same browser, two tabs or a page's parallel requests, carrying the cookie the first one
// just rotated. The cost is the mirror case: a thief who rotates first and an owner who presents the same
// token within the window leave that one replay unrecorded, and the owner signs in again. RefreshTokenHandler
// holds the window as a named value and sets out the trade.
//
// A token is retired by filling RevokedAt, and it is filled once. The successor is created at the instant
// its predecessor was retired, which is how a rotated token is told from one revoked outright; a later
// revocation that overwrote the time would erase that.
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

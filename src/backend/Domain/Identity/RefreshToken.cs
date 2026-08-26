namespace MotsSupplierPortal.Domain.Identity;

/// <summary>
/// A rotating refresh token (docs/architecture/DATABASE-MODEL.md `identity.user_session`).
/// Tokens belong to a family; reuse of a rotated-out token revokes the whole family
/// (docs/backlog/BACKLOG.md STORY-01.1.1 AC3/AC4).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public Guid FamilyId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

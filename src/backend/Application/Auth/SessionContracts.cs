namespace MotsSupplierPortal.Application.Auth;

/// <summary>FR-IAM-007: session management - list active sessions, revoke one or all.</summary>
public sealed record SessionDto(Guid FamilyId, string? Ip, string? UserAgent, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsCurrent);

public interface IListSessionsHandler
{
    Task<IReadOnlyList<SessionDto>> HandleAsync(string? currentRefreshToken, CancellationToken ct);
}

public interface IRevokeSessionHandler
{
    /// <returns>false if no active session with that family id belongs to the caller.</returns>
    Task<bool> HandleAsync(Guid familyId, CancellationToken ct);
}

public interface IRevokeAllSessionsHandler
{
    /// <returns>Number of sessions revoked.</returns>
    Task<int> HandleAsync(string? currentRefreshToken, bool excludeCurrent, CancellationToken ct);
}

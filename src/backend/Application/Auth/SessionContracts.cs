// The vocabulary for session management: listing the sessions a person has open, revoking one or all of them,
// and signing out of the one in this browser.
//
// Each session reports where and when it was created and whether it is the one making the request, so a
// person can tell their own device from the others before revoking anything.
//
// The list is paged by cursor, like every other growing list.
//
// Revoking one answers whether there was an active session with that identifier belonging to the caller.
// Revoking all answers how many live sessions it ended.
//
// Signing out takes the refresh token from the browser's cookie rather than the caller's identity, because the
// cookie is what names the session being left, and it answers nothing: from the browser's side signing out
// always succeeds, whether or not the cookie still named a session.

namespace MotsSupplierPortal.Application.Auth;

using MotsSupplierPortal.Application.Common;

public sealed record SessionDto(Guid FamilyId, string? Ip, string? UserAgent, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsCurrent);

public interface IListSessionsHandler
{
    Task<ListEnvelope<SessionDto>> HandleAsync(string? currentRefreshToken, string? cursor, int? limit, bool withCount, CancellationToken ct);
}

public interface IRevokeSessionHandler
{
    Task<bool> HandleAsync(Guid familyId, CancellationToken ct);
}

public interface IRevokeAllSessionsHandler
{
    Task<int> HandleAsync(string? currentRefreshToken, bool excludeCurrent, CancellationToken ct);
}

public interface ILogoutHandler
{
    Task HandleAsync(string? refreshToken, CancellationToken ct);
}

// Issuing and spending the single-use tokens behind email-verification and password-reset links.
//
// The raw token is the only thing that ever leaves the server, and a link built from it must not also carry
// a user identifier. The token is the lookup key precisely so it does not have to.
//
// Spending one is atomic: a concurrent or repeated call with the same raw token resolves to at most one
// success.

namespace MotsSupplierPortal.Application.Common;

using MotsSupplierPortal.Domain.Identity;

public abstract record ConsumeSecurityTokenResult
{
    public sealed record Success(Guid UserId) : ConsumeSecurityTokenResult;
    public sealed record InvalidOrExpired : ConsumeSecurityTokenResult;
}

public interface ISecurityTokenService
{
    Task<string> IssueAsync(Guid userId, SecurityTokenPurpose purpose, TimeSpan ttl, CancellationToken ct);

    Task<ConsumeSecurityTokenResult> ConsumeAsync(string rawToken, SecurityTokenPurpose purpose, CancellationToken ct);
}

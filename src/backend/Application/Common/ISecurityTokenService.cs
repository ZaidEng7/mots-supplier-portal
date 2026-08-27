using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Application.Common;

public abstract record ConsumeSecurityTokenResult
{
    public sealed record Success(Guid UserId) : ConsumeSecurityTokenResult;
    public sealed record InvalidOrExpired : ConsumeSecurityTokenResult;
}

/// <summary>
/// Issues and consumes the opaque, hashed, single-use tokens used for email-verification and
/// password-reset links (SECURITY-ARCHITECTURE.md §1.6/§1.7). The raw token is the only thing
/// that ever leaves the server - URLs built from it must not also carry a userId.
/// </summary>
public interface ISecurityTokenService
{
    Task<string> IssueAsync(Guid userId, SecurityTokenPurpose purpose, TimeSpan ttl, CancellationToken ct);

    /// <summary>Atomically validates and marks the token consumed - a concurrent or repeat call
    /// with the same raw token always resolves to at most one Success (STORY-02.2.1 AC2).</summary>
    Task<ConsumeSecurityTokenResult> ConsumeAsync(string rawToken, SecurityTokenPurpose purpose, CancellationToken ct);
}

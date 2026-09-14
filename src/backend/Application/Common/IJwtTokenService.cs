// Issuing an access token, and when it expires.
//
// The expiry travels with the token because the interface returns both, so no caller has to work out the
// lifetime from configuration a second time.

namespace MotsSupplierPortal.Application.Common;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    AccessTokenResult IssueAccessToken(
        Guid userId,
        string email,
        Guid? supplierId,
        Guid? organizationId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        IReadOnlyList<string> amr);
}

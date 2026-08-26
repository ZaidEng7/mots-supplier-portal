namespace MotsSupplierPortal.Application.Common;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    AccessTokenResult IssueAccessToken(Guid userId, string email, Guid? supplierId, Guid? organizationId, IReadOnlyList<string> permissions);
}

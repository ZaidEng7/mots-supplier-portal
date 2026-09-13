// Reads the facts that decide which rows a caller may see, off the current request's token.
//
// Who the user is, which supplier they belong to, which organization they belong to, and which
// permissions they hold. Handlers ask this rather than reaching for the request themselves.
//
// HasPermission reads the same claim the permission filter checks. It is the same permission system,
// just readable from inside a handler rather than enforced at the route.

namespace MotsSupplierPortal.Api.Authorization;

using System.IdentityModel.Tokens.Jwt;
using MotsSupplierPortal.Application.Common;

public sealed class HttpScopeContext(IHttpContextAccessor accessor) : IScopeContext
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => Guid.TryParse(User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public Guid? SupplierId => Guid.TryParse(User?.FindFirst("supplierId")?.Value, out var id) ? id : null;

    public Guid? OrganizationId => Guid.TryParse(User?.FindFirst("organizationId")?.Value, out var id) ? id : null;

    public bool HasPermission(string permission) =>
        User?.Claims.Any(c => c.Type == "perms" && c.Value == permission) ?? false;
}

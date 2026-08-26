using System.IdentityModel.Tokens.Jwt;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>Reads the row-scoping context from the current request's JWT claims.</summary>
public sealed class HttpScopeContext(IHttpContextAccessor accessor) : IScopeContext
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => Guid.TryParse(User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public Guid? SupplierId => Guid.TryParse(User?.FindFirst("supplierId")?.Value, out var id) ? id : null;

    public Guid? OrganizationId => Guid.TryParse(User?.FindFirst("organizationId")?.Value, out var id) ? id : null;
}

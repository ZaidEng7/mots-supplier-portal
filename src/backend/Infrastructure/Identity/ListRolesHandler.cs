using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Identity;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>FR-ADM-002: list roles and their current effective permission set (from DB role
/// claims - see PermissionResolver's doc comment for why claims, not the static dictionary, are
/// the source of truth once a role has been seeded). AllPermissions is Permissions.All directly -
/// NOT derived from what roles currently hold - so a permission the catalog knows about but no
/// role has been granted yet still shows up as a grantable (unchecked) option in the admin UI.</summary>
public sealed class ListRolesHandler(RoleManager<IdentityRole<Guid>> roleManager) : IListRolesHandler
{
    public async Task<RolesResponse> HandleAsync(CancellationToken ct)
    {
        // Materialize the role list first: GetClaimsAsync below issues its own query on the same
        // DbContext, which Npgsql rejects while an outer streaming query is still open.
        var allRoles = await roleManager.Roles.ToListAsync(ct);
        var roles = new List<RoleDto>();
        foreach (var role in allRoles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            var permissions = claims.Where(c => c.Type == "perms").Select(c => c.Value).Order().ToList();
            roles.Add(new RoleDto(role.Name!, permissions));
        }
        return new RolesResponse([.. roles.OrderBy(r => r.Name)], Permissions.All);
    }
}

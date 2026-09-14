// Listing the roles and what each one currently grants.
//
// The permissions come from the stored role claims rather than from the shipped map, because an administrator's
// edit has to reach the next sign-in; the permission resolver's own header explains that.
//
// The catalogue of all permissions is the canonical list directly, and is NOT derived from what roles happen to
// hold. So a permission the catalogue knows about but no role has been granted yet still appears as a grantable
// option rather than vanishing from the screen.
//
// The role list is materialised before the claims are read, because reading claims issues its own query on the
// same connection and the provider rejects that while an outer streaming query is still open.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Identity;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListRolesHandler(RoleManager<IdentityRole<Guid>> roleManager) : IListRolesHandler
{
    public async Task<RolesResponse> HandleAsync(CancellationToken ct)
    {
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

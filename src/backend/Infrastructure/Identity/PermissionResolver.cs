using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Resolves a user's effective resource.action permissions from their roles' current DB-stored
/// "perms" claims (docs/architecture/00-foundational-decisions.md §6). Roles are named permission
/// sets, seeded from Roles.DefaultPermissions once at role creation (RoleSeeder) and admin-editable
/// thereafter (FR-ADM-002, ManageRolesHandler) - this is why resolution reads role claims from the
/// database rather than the static dictionary directly: an admin's edit must reach the next login.
/// </summary>
public sealed class PermissionResolver(UserManager<AppUser> userManager, RoleManager<IdentityRole<Guid>> roleManager)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(AppUser user)
    {
        var roleNames = await userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>();
        foreach (var roleName in roleNames)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(c => c.Type == "perms")) permissions.Add(claim.Value);
        }
        return [.. permissions];
    }
}

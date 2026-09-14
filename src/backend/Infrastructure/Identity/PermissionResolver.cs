// Working out what a user may actually do, from the roles they hold.
//
// Roles are named permission sets, seeded once when the role is created and administrator-editable afterwards.
//
// That is why this reads the stored role claims rather than the shipped map: an administrator's edit must reach
// the next sign-in, and the map is only the seed.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

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

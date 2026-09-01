using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Seeds default persona roles on startup (STORY-01.7.1 AC3). Roles.DefaultPermissions
/// is applied as role claims (type "perms", same claim type PermissionResolver reads back) exactly
/// once per role, marked by a "perms:seeded" claim - NOT inferred from "does this role already
/// have any perms claims", because ministry_viewer's correct default is deliberately zero
/// permissions (MSP-62/BRULE-086) and an emptiness check would re-seed it forever. The marker also
/// makes this safe to run against a database created before role-claim seeding existed at all: a
/// role that predates this change has no marker either, so it gets backfilled exactly once here
/// rather than silently starting every user's JWT with zero permissions.</summary>
public static class RoleSeeder
{
    private const string SeededMarkerClaimType = "perms:seeded";

    public static async Task SeedAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var (roleName, permissions) in Roles.DefaultPermissions)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName) { Id = Guid.CreateVersion7() };
                await roleManager.CreateAsync(role);
            }

            var claims = await roleManager.GetClaimsAsync(role);
            if (claims.Any(c => c.Type == SeededMarkerClaimType)) continue;

            foreach (var permission in permissions)
            {
                await roleManager.AddClaimAsync(role, new Claim("perms", permission));
            }
            await roleManager.AddClaimAsync(role, new Claim(SeededMarkerClaimType, "true"));
        }
    }
}

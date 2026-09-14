// Seeding each role's default permissions at startup, without ever undoing an administrator's edit.
//
// The shipped map is the source. The sets are administrator-editable afterwards.
//
//
// EACH PERMISSION IS MARKED, NOT EACH ROLE, AND THAT IS A CORRECTION
//
// It used to write one marker per role and skip the whole role on every later start. That made the defaults a
// one-time snapshot: adding a permission to a role in code had no effect on any environment whose roles
// already existed.
//
// Found when a new dashboard gave the ministry viewer its first permission. The tests passed against a fresh
// database and failed against a reused one, and the same divergence would have shipped as "the ministry
// dashboard is forbidden in production and works locally".
//
//
// THE MARKER IS WHAT KEEPS A REMOVAL INTACT
//
// A permission whose marker exists has been offered once. If it is absent from the role now, somebody took it
// away deliberately, and re-adding it would overrule them.
//
// A permission with no marker has never been offered, so it is new in code and gets added.
//
// A deployment seeded under the old per-role marker has no per-permission markers at all. Everything it
// currently HOLDS is treated as already offered, so the pass adds only what is genuinely new and does not
// resurrect anything an administrator removed before now.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

public static class RoleSeeder
{
    private const string LegacyRoleMarkerClaimType = "perms:seeded";

    private const string PermissionMarkerClaimType = "perms:offered";

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
            var alreadyOffered = claims
                .Where(c => c.Type == PermissionMarkerClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

            if (alreadyOffered.Count == 0 && claims.Any(c => c.Type == LegacyRoleMarkerClaimType))
            {
                foreach (var legacyPermission in claims.Where(c => c.Type == "perms").Select(c => c.Value))
                {
                    alreadyOffered.Add(legacyPermission);
                    await roleManager.AddClaimAsync(role, new Claim(PermissionMarkerClaimType, legacyPermission));
                }
            }

            var held = claims.Where(c => c.Type == "perms").Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var permission in permissions.Where(p => !alreadyOffered.Contains(p)))
            {
                if (!held.Contains(permission))
                {
                    await roleManager.AddClaimAsync(role, new Claim("perms", permission));
                }
                await roleManager.AddClaimAsync(role, new Claim(PermissionMarkerClaimType, permission));
            }
        }
    }
}

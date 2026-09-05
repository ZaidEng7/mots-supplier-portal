using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Seeds default persona roles on startup (STORY-01.7.1 AC3). <c>Roles.DefaultPermissions</c> is the
/// source; the sets are admin-editable afterwards, and this must never undo an edit.
///
/// <para><b>Marked PER PERMISSION, not per role, and that is a correction.</b> It used to write one
/// <c>perms:seeded</c> claim per role and skip the whole role on every later start. That made the
/// defaults a one-time snapshot: adding a permission to a role in code had no effect on any
/// environment whose roles already existed. Found when EPIC-18 gave <c>ministry_viewer</c> its first
/// permission - the tests passed against a fresh database and failed against a reused one, and the
/// same divergence would have shipped as "the Ministry dashboard 403s in production and works
/// locally". See DECISIONS-TAKEN.md D-30.</para>
///
/// <para>The per-permission marker is what keeps an admin's REMOVAL intact. A permission whose marker
/// exists has been offered once; if it is absent from the role now, someone took it away deliberately
/// and re-adding it would overrule them. A permission with no marker has never been offered, so it is
/// new in code and gets added.</para>
/// </summary>
public static class RoleSeeder
{
    /// <summary>The old per-role marker. Still recognised, so an existing deployment's roles are not
    /// re-offered every permission they have ever had - see MigrateLegacyMarker.</summary>
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

            // A deployment seeded under the old per-role marker has no per-permission markers at all.
            // Treat everything it currently HOLDS as already offered, so this pass adds only what is
            // genuinely new in code and does not resurrect anything an admin removed before now.
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

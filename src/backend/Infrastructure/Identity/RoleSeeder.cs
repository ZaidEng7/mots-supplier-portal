using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>Seeds default persona roles on startup (STORY-01.7.1 AC3).</summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var roleName in Roles.DefaultPermissions.Keys)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.CreateVersion7() });
            }
        }
    }
}

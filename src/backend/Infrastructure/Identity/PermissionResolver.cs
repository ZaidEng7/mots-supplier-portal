using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Resolves a user's effective resource.action permissions from their seeded persona roles
/// (docs/architecture/00-foundational-decisions.md §6). Roles are named permission sets.
/// </summary>
public sealed class PermissionResolver(UserManager<AppUser> userManager)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return [.. roles
            .Where(Roles.DefaultPermissions.ContainsKey)
            .SelectMany(r => Roles.DefaultPermissions[r])
            .Distinct()];
    }
}

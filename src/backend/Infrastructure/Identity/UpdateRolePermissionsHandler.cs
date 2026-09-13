// Replacing a role's permission set.
//
// Two guards.
//
// Every requested permission must be in the canonical catalogue. A typo, or a stale caller, must not silently
// grant an unrecognised claim that the route's own permission filter would still honour.
//
// And the update must not leave no role at all holding the permission to manage roles. That is a self-lockout
// which would make role management itself unrecoverable without a database console.
//
// The audit row carries the old set and the new one as a structured difference, so "who took this permission
// away" is queryable rather than parseable.

namespace MotsSupplierPortal.Infrastructure.Identity;

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Identity;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class UpdateRolePermissionsHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    IScopeContext scope,
    IAuditLogger auditLogger,
    AppDbContext db) : IUpdateRolePermissionsHandler
{
    public async Task<UpdateRolePermissionsResult> HandleAsync(UpdateRolePermissionsCommand command, CancellationToken ct)
    {
        var requested = command.Permissions.Distinct().ToList();
        foreach (var permission in requested)
        {
            if (!Permissions.All.Contains(permission))
            {
                return new UpdateRolePermissionsResult.InvalidPermission(permission);
            }
        }

        var role = await roleManager.FindByNameAsync(command.RoleName);
        if (role is null) return new UpdateRolePermissionsResult.NotFound();

        if (requested.Contains(Permissions.AdminRolesManage) is false)
        {
            var otherRoles = await roleManager.Roles.Where(r => r.Id != role.Id).ToListAsync(ct);
            var anyOtherRoleHasIt = false;
            foreach (var other in otherRoles)
            {
                var otherClaims = await roleManager.GetClaimsAsync(other);
                if (otherClaims.Any(c => c.Type == "perms" && c.Value == Permissions.AdminRolesManage))
                {
                    anyOtherRoleHasIt = true;
                    break;
                }
            }
            if (!anyOtherRoleHasIt) return new UpdateRolePermissionsResult.WouldLockOutRoleManagement();
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims.Where(c => c.Type == "perms").Select(c => c.Value).Order().ToList();

        foreach (var claim in existingClaims.Where(c => c.Type == "perms"))
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }
        foreach (var permission in requested)
        {
            await roleManager.AddClaimAsync(role, new Claim("perms", permission));
        }

        var changes = AuditChangeBuilder.Build(("permissions", string.Join(",", existingPermissions), string.Join(",", requested.Order())));
        await auditLogger.LogAsync("Role", role.Id, "role_permissions_updated", scope.UserId, toState: role.Name, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);

        return new UpdateRolePermissionsResult.Success(new RoleDto(role.Name!, requested.Order().ToList()));
    }
}

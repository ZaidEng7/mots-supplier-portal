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

/// <summary>FR-ADM-002: replace a role's permission set. Two guards: every requested permission
/// must be in the canonical Permissions.All catalog (a typo or stale client must not silently
/// grant an unrecognized claim that PermissionEndpointFilter would still honor), and the update
/// must not leave zero roles holding Permissions.AdminRolesManage (a self-lockout that would make
/// role management itself unrecoverable without a DB console).</summary>
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

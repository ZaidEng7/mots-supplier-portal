namespace MotsSupplierPortal.Application.Identity;

public sealed record RoleDto(string Name, IReadOnlyList<string> Permissions);

/// <summary>FR-ADM-002 bug fix: AllPermissions is the canonical Permissions.All catalog, not the
/// union of what roles happen to already hold. A permission newly added to the catalog but not
/// yet granted to any role (e.g. right after a code change, before an admin has granted it) must
/// still be listed here as an available-but-unchecked option - otherwise the admin UI has no way
/// to ever grant it short of a direct DB write, which defeats the point of a roles admin UI.</summary>
public sealed record RolesResponse(IReadOnlyList<RoleDto> Roles, IReadOnlyList<string> AllPermissions);

public sealed record UpdateRolePermissionsCommand(string RoleName, IReadOnlyList<string> Permissions);

public abstract record UpdateRolePermissionsResult
{
    public sealed record Success(RoleDto Role) : UpdateRolePermissionsResult;
    public sealed record NotFound : UpdateRolePermissionsResult;
    /// <summary>AC4-equivalent guard: a permission outside the canonical Permissions.All catalog
    /// was requested - rejected rather than silently persisted, since PermissionEndpointFilter
    /// trusts whatever string sits in a "perms" claim.</summary>
    public sealed record InvalidPermission(string Permission) : UpdateRolePermissionsResult;
    /// <summary>Privilege-escalation/lockout guard: this update would leave zero roles holding
    /// Permissions.AdminRolesManage, meaning no one could ever edit a role's permissions again.</summary>
    public sealed record WouldLockOutRoleManagement : UpdateRolePermissionsResult;
}

public interface IListRolesHandler
{
    Task<RolesResponse> HandleAsync(CancellationToken ct);
}

public interface IUpdateRolePermissionsHandler
{
    Task<UpdateRolePermissionsResult> HandleAsync(UpdateRolePermissionsCommand command, CancellationToken ct);
}

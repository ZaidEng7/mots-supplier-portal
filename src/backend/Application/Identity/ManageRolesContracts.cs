namespace MotsSupplierPortal.Application.Identity;

public sealed record RoleDto(string Name, IReadOnlyList<string> Permissions);

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
    Task<IReadOnlyList<RoleDto>> HandleAsync(CancellationToken ct);
}

public interface IUpdateRolePermissionsHandler
{
    Task<UpdateRolePermissionsResult> HandleAsync(UpdateRolePermissionsCommand command, CancellationToken ct);
}

// The vocabulary for editing what a role grants.
//
// The list of available permissions is the canonical catalogue, not the union of what roles happen to
// hold already. A permission newly added to the catalogue but not yet granted to anybody must still
// appear as an option, or the only way to grant it is a direct database write, which defeats the point
// of having a screen.
//
// Two guards on an update, and both are refusals rather than silent corrections.
//
// A permission outside the catalogue is refused rather than stored, because the route filter trusts
// whatever string sits in a token's claims, so an invented permission would become real.
//
// An update that would leave no role able to edit roles is refused. Otherwise one save could lock
// everybody out of role management permanently, with no way back short of the database.

namespace MotsSupplierPortal.Application.Identity;

public sealed record RoleDto(string Name, IReadOnlyList<string> Permissions);

public sealed record RolesResponse(IReadOnlyList<RoleDto> Roles, IReadOnlyList<string> AllPermissions);

public sealed record UpdateRolePermissionsCommand(string RoleName, IReadOnlyList<string> Permissions);

public abstract record UpdateRolePermissionsResult
{
    public sealed record Success(RoleDto Role) : UpdateRolePermissionsResult;
    public sealed record NotFound : UpdateRolePermissionsResult;
    public sealed record InvalidPermission(string Permission) : UpdateRolePermissionsResult;
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

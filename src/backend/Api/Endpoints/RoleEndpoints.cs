// Lists the roles and edits what a role grants. System administrators only.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Identity;
using MotsSupplierPortal.Domain.Identity;

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

public sealed class UpdateRolePermissionsRequestValidator : AbstractValidator<UpdateRolePermissionsRequest>
{
    public UpdateRolePermissionsRequestValidator()
    {
        RuleFor(x => x.Permissions).NotNull();
    }
}

public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/roles").WithTags("Admin");

        group.MapGet("/", async (IListRolesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminRolesManage)
        .WithName("ListRoles");

        group.MapPut("/{roleName}/permissions", async (
            string roleName,
            UpdateRolePermissionsRequest request,
            IUpdateRolePermissionsHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateRolePermissionsCommand(roleName, request.Permissions), ct);
            return result switch
            {
                UpdateRolePermissionsResult.Success s => Results.Ok(s.Role),
                UpdateRolePermissionsResult.NotFound => Results.NotFound(),
                UpdateRolePermissionsResult.InvalidPermission p => Results.BadRequest(new { error = "invalid_permission", permission = p.Permission }),
                UpdateRolePermissionsResult.WouldLockOutRoleManagement => Results.BadRequest(new { error = "would_lock_out_role_management" }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.AdminRolesManage)
        .Validate<UpdateRolePermissionsRequest>()
        .WithName("UpdateRolePermissions");
    }
}

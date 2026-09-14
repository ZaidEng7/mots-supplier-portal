// The permission check every route goes through, and the small extension methods that attach it.
//
// The interface re-checks the same permission only to hide buttons. It is never the source of truth;
// this is.
//
// The check passes when the caller holds any one of the listed permissions, and the list is normally
// one. The multi-permission form exists for a read whose audience is wider than its writes. Listing
// evaluation templates is needed by anyone who may bind one to a tender as well as by whoever
// maintains them, and gating that read on the management permission alone left an officer able to
// bind a template they could not see, so the list came back refused and the tender could not reach
// internal review at all.
//
// RequiredPermissionsMetadata records the same requirement on the route table, not only in the
// filter. A filter is invisible to anything that does not send a request, so without this nothing
// could ask the application which permission gates which route. The permissions document is generated
// by searching this project's own source for exactly that reason, and the authorisation sweep needs
// the same fact while running: to check that a role lacking a permission is refused, a test has to
// know what each route wants, and reading it off the route is the only way that cannot drift from
// what the filter actually enforces.
//
// The group-level variant applies one permission to every route in a group, which is the same thing
// as applying the single-route version to each one, without repeating it.

namespace MotsSupplierPortal.Api.Authorization;

public sealed class PermissionEndpointFilter(params string[] permissions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var hasPermission = user.Claims.Any(c => c.Type == "perms" && permissions.Contains(c.Value));
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public sealed class RequiredPermissionsMetadata(params string[] permissions)
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

public static class RequirePermissionExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder
            .AddEndpointFilter(new PermissionEndpointFilter(permission))
            .WithMetadata(new RequiredPermissionsMetadata(permission))
            .RequireAuthorization();

    public static RouteHandlerBuilder RequireAnyPermission(this RouteHandlerBuilder builder, params string[] permissions) =>
        builder
            .AddEndpointFilter(new PermissionEndpointFilter(permissions))
            .WithMetadata(new RequiredPermissionsMetadata(permissions))
            .RequireAuthorization();

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission) =>
        builder
            .AddEndpointFilter(new PermissionEndpointFilter(permission))
            .WithMetadata(new RequiredPermissionsMetadata(permission))
            .RequireAuthorization();
}

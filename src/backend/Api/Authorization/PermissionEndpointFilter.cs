namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Enforces a resource.action permission claim at the API (STORY-01.7.1). The UI re-checks the
/// same permission solely to hide affordances - it is never the source of truth.
/// </summary>
public sealed class PermissionEndpointFilter(string permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var hasPermission = user.Claims.Any(c => c.Type == "perms" && c.Value == permission);
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class RequirePermissionExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.AddEndpointFilter(new PermissionEndpointFilter(permission)).RequireAuthorization();

    /// <summary>Group-level variant for an endpoint group where every route shares the same
    /// permission (e.g. EvaluationTemplateEndpoints) - equivalent to applying the single-route
    /// overload to each MapGet/MapPost/etc. individually, just without repeating it per route.</summary>
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission) =>
        builder.AddEndpointFilter(new PermissionEndpointFilter(permission)).RequireAuthorization();
}

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Enforces a resource.action permission claim at the API (STORY-01.7.1). The UI re-checks the
/// same permission solely to hide affordances - it is never the source of truth.
/// </summary>
public sealed class PermissionEndpointFilter(params string[] permissions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        // ANY of the listed permissions, and the list is normally one.
        //
        // The multi-permission form exists for a read whose audience is wider than its writes: listing
        // evaluation templates is needed by anyone who may BIND one to a tender (rfq.edit) as well as by
        // whoever maintains them (evaluation.template.manage). Gating the read on the management
        // permission alone left a procurement officer able to bind a template they could not see, so
        // the list came back 403 and the tender could not reach internal review at all.
        var hasPermission = user.Claims.Any(c => c.Type == "perms" && permissions.Contains(c.Value));
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

    /// <summary>Passes when the caller holds ANY of these. For a read that several roles legitimately
    /// need while its writes stay narrower - see the filter's own comment.</summary>
    public static RouteHandlerBuilder RequireAnyPermission(this RouteHandlerBuilder builder, params string[] permissions) =>
        builder.AddEndpointFilter(new PermissionEndpointFilter(permissions)).RequireAuthorization();

    /// <summary>Group-level variant for an endpoint group where every route shares the same
    /// permission (e.g. EvaluationTemplateEndpoints) - equivalent to applying the single-route
    /// overload to each MapGet/MapPost/etc. individually, just without repeating it per route.</summary>
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission) =>
        builder.AddEndpointFilter(new PermissionEndpointFilter(permission)).RequireAuthorization();
}

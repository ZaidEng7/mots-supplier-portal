// The administrator's editable field configuration: which supplier fields re-open a compliance review
// when edited, and which legal-information fields are required.
//
// The single-item read exists because the update's precondition depends on it. It was added in the same
// change as the precondition rather than afterwards, because a precondition nobody can obtain refuses
// every caller, which is a mistake this project has already made once on another resource.
//
// These rows are worth a precondition because they decide whether editing a bank account re-opens a
// compliance review. Two administrators tightening and loosening the same control at once is exactly the
// race worth refusing rather than resolving in favour of whoever saved second.
//
// The update returns the new version on its own response, so a second change to the same row has a
// precondition to send without waiting for a re-read.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public sealed record UpdateFieldConfigRequest(bool IsEnabled);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/field-config").WithTags("Admin");

        group.MapGet("/", async (string? category, IGetFieldConfigHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(category, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetFieldConfig");

        group.MapGet("/{category}/{fieldCode}", async (
            string category, string fieldCode, IGetOneFieldConfigHandler handler, CancellationToken ct) =>
        {
            var config = await handler.HandleAsync(category, fieldCode, ct);
            return config is null ? Results.NotFound() : Results.Ok(config);
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithETag()
        .WithName("GetOneFieldConfig");

        group.MapPut("/{category}/{fieldCode}", async (
            string category,
            string fieldCode,
            UpdateFieldConfigRequest request,
            IUpdateFieldConfigHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(category, fieldCode, request.IsEnabled, ct);
            return result switch
            {
                UpdateFieldConfigResult.Success s => Results.Ok(s.Config),
                UpdateFieldConfigResult.NotFound => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .RequireIfMatch()
        .WithETag()
        .WithFreshETag()
.WithName("UpdateFieldConfig");
    }
}

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record UpdateFieldConfigRequest(bool IsEnabled);

/// <summary>FEAT-04.9/FEAT-04.2: admin-editable field config (compliance re-trigger list,
/// LegalInfo requiredness) - see SupplierFieldConfig's own doc comment for why this exists.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/field-config").WithTags("Admin");

        group.MapGet("/", async (string? category, IGetFieldConfigHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(category, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetFieldConfig");

        // T-029: the single-item read the PUT's If-Match depends on. Added in the same change as the
        // guard, not after it - a precondition nobody can obtain refuses every caller (batch 3,
        // Offering).
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
        // These rows decide whether editing a bank account re-triggers compliance review. Two
        // administrators tightening and loosening the same control at once is exactly the race worth
        // refusing rather than resolving in favour of whoever saved second.
        .RequireIfMatch()
        .WithETag()
        .WithName("UpdateFieldConfig");
    }
}

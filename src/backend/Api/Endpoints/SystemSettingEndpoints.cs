// Reading and writing the system settings. A system administrator sees and changes all of them; everybody
// else, including a visitor who has not signed in, reads a small allowed subset.
//
// The public read exists because two of these settings govern screens that render before anyone has signed
// in: whether the registration form should be offered at all, and which currency a bid defaults to.
// Without it the interface would keep its own copy of both, and the copy would be the one that goes stale.
//
// The subset is built from an explicit allow-list rather than a filter, so a setting added later is
// invisible to the public read until somebody decides otherwise. That is the direction that fails safely.
//
// A key that is not in the catalogue answers not-found rather than a validation failure. The caller asked
// for something that does not exist, and telling them their value was invalid would send them looking in
// the wrong place.
//
// The finance-system status route is signed in but gated by no permission, because the handler answers the
// narrowest thing the caller is entitled to know. There is no wider answer for a gate to protect.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Configuration;
using MotsSupplierPortal.Domain.Identity;

public sealed record UpdateSystemSettingRequest(string Value);

public static class SystemSettingEndpoints
{
    public static void MapSystemSettingEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/settings").WithTags("Admin");

        admin.MapGet("/", async (ISystemSettingAdminHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(ct)))
            .RequirePermission(Permissions.ReferenceDataManage)
            .WithName("ListSystemSettings");

        admin.MapPut("/{key}", async (
            string key, UpdateSystemSettingRequest request,
            ISystemSettingAdminHandler handler, CancellationToken ct) =>
        {
            var result = await handler.UpdateAsync(new UpdateSystemSettingCommand(key, request.Value), ct);
            return result switch
            {
                SystemSettingResult.Success s => Results.Ok(s.Setting),
                SystemSettingResult.UnknownKey => Results.NotFound(),
                SystemSettingResult.Invalid invalid =>
                    Results.UnprocessableEntity(new { error = "invalid_setting_value", reason = invalid.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("UpdateSystemSetting");

        app.MapGet("/api/v1/system/status", async (
            MotsSupplierPortal.Application.Platform.ISystemStatusHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequireAuthorization()
        .WithName("GetSystemStatus");

        app.MapGet("/api/v1/reference/settings", async (ISystemSettingAdminHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ReadPublicAsync(ct)))
            .AllowAnonymous()
            .WithName("GetPublicSettings")
            .WithTags("Reference");
    }
}

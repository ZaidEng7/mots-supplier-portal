using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Configuration;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record UpdateSystemSettingRequest(string Value);

/// <summary>
/// FR-ADM-006/T-060. `system_admin` reads and writes the settings catalogue; everyone else - including
/// an unauthenticated visitor - reads the allow-listed public subset.
///
/// <para>The public read exists because two of these settings govern screens that render before
/// anyone has signed in: whether the registration form should be offered at all, and which currency
/// a proposal defaults to. Without it the SPA would keep its own copy of both, and the copy would be
/// the one that goes stale.</para>
/// </summary>
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
                // A key that is not in the catalogue is not a resource here, and saying so is a 404
                // rather than a 422: the caller asked for something that does not exist, and telling
                // them their VALUE was invalid would send them looking in the wrong place.
                SystemSettingResult.UnknownKey => Results.NotFound(),
                SystemSettingResult.Invalid invalid =>
                    Results.UnprocessableEntity(new { error = "invalid_setting_value", reason = invalid.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("UpdateSystemSetting");

        // Public, for the same stated reason the reference lists are (MSP-67): the registration form
        // is unauthenticated and has to know whether it should be shown. The response is built from
        // SystemSettings.PubliclyReadable, an allow-list - a setting added later is invisible here
        // until someone decides otherwise, which is the direction that fails safely.
        app.MapGet("/api/v1/reference/settings", async (ISystemSettingAdminHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ReadPublicAsync(ct)))
            .AllowAnonymous()
            .WithName("GetPublicSettings")
            .WithTags("Reference");
    }
}

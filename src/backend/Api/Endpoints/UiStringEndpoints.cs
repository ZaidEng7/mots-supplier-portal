using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>SCR-716's write. One value; the key and language are in the path.</summary>
public sealed record UpsertUiStringRequest(string Value);

public sealed class UpsertUiStringRequestValidator : AbstractValidator<UpsertUiStringRequest>
{
    public UpsertUiStringRequestValidator()
    {
        // Non-empty, because an override to the empty string would blank a label with no way for a user
        // to tell it from a missing translation. Removing the override is how you go back.
        RuleFor(x => x.Value).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>
/// SCR-716: administrator rewordings of shipped interface strings.
///
/// <para>The read is anonymous and the writes are system_admin. See UiStringBundleDto for why the read
/// has to be: the login screen renders before anyone is authenticated, and a reworded label that only
/// appeared after sign-in would be a worse inconsistency than none.</para>
/// </summary>
public static class UiStringEndpoints
{
    /// <summary>The two the product ships, and the same set UpdateAccountRequestValidator accepts.</summary>
    private static readonly string[] SupportedLanguages = ["ar", "en"];

    public static void MapUiStringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/ui-strings/{language}", async (
            string language, IGetUiStringBundleHandler handler, CancellationToken ct) =>
        {
            // A language outside the shipped set is a 404 rather than an empty bundle: an empty bundle is
            // a legitimate answer meaning "no overrides", and answering it for "fr" would tell a caller
            // that French exists and simply has no rewordings.
            if (!SupportedLanguages.Contains(language)) return Results.NotFound();

            return Results.Ok(await handler.HandleAsync(language, ct));
        })
        .AllowAnonymous()
        .WithTags("Admin")
        .WithName("GetUiStringBundle");

        var admin = app.MapGroup("/api/v1/admin/ui-strings").WithTags("Admin");

        admin.MapGet("/", async (IListUiStringOverridesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ListUiStringOverrides");

        admin.MapPut("/{language}/{*key}", async (
            string language,
            string key,
            UpsertUiStringRequest request,
            IValidator<UpsertUiStringRequest> validator,
            IUpsertUiStringOverrideHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);
            if (!SupportedLanguages.Contains(language)) return Results.NotFound();
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            // The key is a catch-all route segment because i18n keys contain dots and slashes are the only
            // thing they do not - `proposal.errors.reviseFailed` has to arrive whole.
            var updated = await handler.HandleAsync(new UpsertUiStringCommand(key, language, request.Value, userId), ct);
            return Results.Ok(updated);
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("UpsertUiStringOverride");

        admin.MapDelete("/{language}/{*key}", async (
            string language, string key, IDeleteUiStringOverrideHandler handler, CancellationToken ct) =>
            // 404 when there was nothing to remove, rather than a cheerful 204: "the shipped string is
            // back" and "there was never an override" are different answers to the same click.
            await handler.HandleAsync(key, language, ct) ? Results.NoContent() : Results.NotFound())
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("DeleteUiStringOverride");
    }
}

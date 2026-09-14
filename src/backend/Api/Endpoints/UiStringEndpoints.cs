// Reading and rewording the interface's own labels. The read is open to anybody; the writes are
// system-administrator only.
//
// The read has to be open. The sign-in screen renders before anyone has signed in, and a reworded label
// that only appeared after signing in would be a worse inconsistency than none at all.
//
// A new value cannot be empty. An override to an empty string would blank a label with no way for a user
// to tell it from a missing translation. Removing the override is how you go back.
//
// A language outside the two the product ships answers not-found rather than an empty set. An empty set is
// a legitimate answer meaning there are no overrides, and giving it for an unsupported language would tell
// a caller that language exists and simply has no rewordings.
//
// The key arrives as a catch-all path segment, because these keys contain dots and a slash is the only
// character they do not, so a key has to arrive whole.
//
// Deleting an override that was not there answers not-found rather than success, for the same reason the
// email templates do: "the shipped string is back" and "there was never an override" are different
// answers to the same click.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

public sealed record UpsertUiStringRequest(string Value);

public sealed class UpsertUiStringRequestValidator : AbstractValidator<UpsertUiStringRequest>
{
    public UpsertUiStringRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().MaximumLength(2000);
    }
}

public static class UiStringEndpoints
{
    private static readonly string[] SupportedLanguages = ["ar", "en"];

    public static void MapUiStringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/ui-strings/{language}", async (
            string language, IGetUiStringBundleHandler handler, CancellationToken ct) =>
        {
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
            IUpsertUiStringOverrideHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            if (!SupportedLanguages.Contains(language)) return Results.NotFound();
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            var updated = await handler.HandleAsync(new UpsertUiStringCommand(key, language, request.Value, userId), ct);
            return Results.Ok(updated);
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .Validate<UpsertUiStringRequest>()
        .WithName("UpsertUiStringOverride");

        admin.MapDelete("/{language}/{*key}", async (
            string language, string key, IDeleteUiStringOverrideHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(key, language, ct) ? Results.NoContent() : Results.NotFound())
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("DeleteUiStringOverride");
    }
}

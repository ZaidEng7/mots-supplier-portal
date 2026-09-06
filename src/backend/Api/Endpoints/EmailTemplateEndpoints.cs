using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record UpsertEmailTemplateRequest(string SubjectAr, string SubjectEn, string BodyAr, string BodyEn);

public sealed class UpsertEmailTemplateRequestValidator : AbstractValidator<UpsertEmailTemplateRequest>
{
    public UpsertEmailTemplateRequestValidator()
    {
        // Both locales required, for T-061's reason: an Arabic-only subject renders blank for an English
        // recipient, and an email with no subject line is the one that gets filtered.
        RuleFor(x => x.SubjectAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SubjectEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(4000);
    }
}

/// <summary>
/// T-076: the 23 transactional email bodies, admin-editable, with a per-template required-token contract.
///
/// <para>Split out of T-061 because the in-app catalogue's token rule was not enough here: an in-app
/// notification that loses a token reads badly, and an email that loses <c>{verifyUrl}</c> locks the
/// recipient out of the account they are creating - with nothing in the system able to tell that it
/// happened, since the send succeeded and the body was valid HTML.</para>
/// </summary>
public static class EmailTemplateEndpoints
{
    public static void MapEmailTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/email-templates").WithTags("Admin");

        group.MapGet("/", async (IListEmailTemplatesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ListEmailTemplates");

        group.MapPut("/{key}", async (
            string key,
            UpsertEmailTemplateRequest request,
            IValidator<UpsertEmailTemplateRequest> validator,
            IUpsertEmailTemplateHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            var result = await handler.HandleAsync(new UpsertEmailTemplateCommand(
                key, request.SubjectAr, request.SubjectEn, request.BodyAr, request.BodyEn, userId), ct);

            return result switch
            {
                UpsertEmailTemplateResult.Success s => Results.Ok(s.Override),
                UpsertEmailTemplateResult.UnknownKey => Results.NotFound(),
                // 422 with the tokens NAMED, through a problem+json result rather than an anonymous body:
                // §7's middleware reshapes every non-2xx and an anonymous object's fields do not survive it.
                // See TokenContractResult, which exists because the first version lost them.
                UpsertEmailTemplateResult.MissingRequiredTokens m => TokenContractResult.MissingRequired(m.MissingTokens),
                UpsertEmailTemplateResult.UnknownTokens u => TokenContractResult.Unknown(u.Tokens),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("UpsertEmailTemplate");

        group.MapDelete("/{key}", async (
            string key, IDeleteEmailTemplateHandler handler, CancellationToken ct) =>
            // 404 when there was no override: "the shipped words are back" and "there was never an override"
            // are different answers to the same click.
            await handler.HandleAsync(key, ct) ? Results.NoContent() : Results.NotFound())
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("DeleteEmailTemplate");
    }
}

// Reading and rewording the twenty-three transactional emails, with a check on the placeholders each one
// cannot be sent without.
//
// This is separate from the in-app notification copy because the placeholder rule is not the same. An
// in-app notification that loses a placeholder reads badly. An email that loses its verification link
// locks the recipient out of the account they are creating, and nothing in the system can tell that it
// happened, because the send succeeded and the body was valid.
//
// Both languages are required, for the same reason as the notification copy: an Arabic-only subject
// renders blank for an English-speaking recipient, and an email with no subject line is the one that gets
// filtered.
//
// A placeholder failure is refused with the offending placeholders named, through a proper failure result
// rather than a plain object, because the middleware reshapes every failure and a plain object's fields do
// not survive it. That result type exists because the first version lost them.
//
// Deleting an override that was not there answers not-found rather than success. "The shipped words are
// back" and "there was never an override" are different answers to the same click.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

public sealed record UpsertEmailTemplateRequest(string SubjectAr, string SubjectEn, string BodyAr, string BodyEn);

public sealed class UpsertEmailTemplateRequestValidator : AbstractValidator<UpsertEmailTemplateRequest>
{
    public UpsertEmailTemplateRequestValidator()
    {
        RuleFor(x => x.SubjectAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SubjectEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(4000);
    }
}

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
            IUpsertEmailTemplateHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            var result = await handler.HandleAsync(new UpsertEmailTemplateCommand(
                key, request.SubjectAr, request.SubjectEn, request.BodyAr, request.BodyEn, userId), ct);

            return result switch
            {
                UpsertEmailTemplateResult.Success s => Results.Ok(s.Override),
                UpsertEmailTemplateResult.UnknownKey => Results.NotFound(),
                UpsertEmailTemplateResult.MissingRequiredTokens m => TokenContractResult.MissingRequired(m.MissingTokens),
                UpsertEmailTemplateResult.UnknownTokens u => TokenContractResult.Unknown(u.Tokens),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .Validate<UpsertEmailTemplateRequest>()
        .WithName("UpsertEmailTemplate");

        group.MapDelete("/{key}", async (
            string key, IDeleteEmailTemplateHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(key, ct) ? Results.NoContent() : Results.NotFound())
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("DeleteEmailTemplate");
    }
}

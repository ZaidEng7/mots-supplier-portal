// Reading and rewording the notification copy, per notification type, in both languages.
//
// Both languages are always required. A notification with an Arabic title and no English one would render
// blank for an English-speaking user, and this product's own fallback is Arabic-first rather than empty,
// so the refusal belongs here rather than in whatever renders it.
//
// The title length limit is 300 rather than a tighter number of this endpoint's own choosing, because the
// message explaining that limit is shared with other endpoints and its approved Arabic says 300. A tighter
// number here would answer a user with a message stating the wrong limit.
//
// Deleting removes the override and restores the shipped wording, which is why a delete exists here and
// does not on reference data. Nothing points at an override, and the words underneath it never went away.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Identity;

public sealed record NotificationTemplateRequest(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

public sealed class NotificationTemplateRequestValidator : AbstractValidator<NotificationTemplateRequest>
{
    public NotificationTemplateRequestValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(1000);
    }
}

public static class NotificationTemplateEndpoints
{
    public static void MapNotificationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/notification-templates").WithTags("Admin");

        group.MapGet("/", async (INotificationTemplateAdminHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(ct)))
            .RequirePermission(Permissions.ReferenceDataManage)
            .WithName("ListNotificationTemplates");

        group.MapPut("/{type}", async (
            string type, NotificationTemplateRequest request,
            IValidator<NotificationTemplateRequest> validator,
            INotificationTemplateAdminHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return Map(await handler.UpdateAsync(new UpdateNotificationTemplateCommand(
                type, request.TitleAr, request.TitleEn, request.BodyAr, request.BodyEn), ct));
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("UpdateNotificationTemplate");

        group.MapDelete("/{type}", async (
            string type, INotificationTemplateAdminHandler handler, CancellationToken ct) =>
            Map(await handler.RevertAsync(type, ct)))
            .RequirePermission(Permissions.ReferenceDataManage)
            .WithName("RevertNotificationTemplate");
    }

    private static IResult Map(NotificationTemplateResult result) => result switch
    {
        NotificationTemplateResult.Success s => Results.Ok(s.Template),
        NotificationTemplateResult.UnknownType => Results.NotFound(),
        NotificationTemplateResult.UnknownTokens unknown => Results.UnprocessableEntity(new
        {
            error = "unknown_tokens",
            message = $"This notification cannot fill: {string.Join(", ", unknown.Tokens.Select(t => $"{{{t}}}"))}.",
            tokens = unknown.Tokens,
        }),
        _ => Results.Problem(),
    };
}

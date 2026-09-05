using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record NotificationTemplateRequest(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

public sealed class NotificationTemplateRequestValidator : AbstractValidator<NotificationTemplateRequest>
{
    public NotificationTemplateRequestValidator()
    {
        // Both locales required, always. A notification with an Arabic title and no English one would
        // render blank for an English-language user, and this product's own fallback is Arabic-first
        // rather than empty - so the refusal belongs here, not in the reader.
        // 300, not a tighter number of this endpoint's own choosing: `TitleAr.MaximumLength` is a
        // SHARED catalogue key whose approved Arabic says 300 characters (the RFQ requests set it).
        // A 200 here would answer a supplier with a message stating the wrong limit.
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>FR-ADM-007/T-061/SCR-715. Notification copy, per type, in both locales.</summary>
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

        // DELETE removes the OVERRIDE, restoring the shipped copy - which is why a delete exists here
        // and does not on reference data (D-28). Nothing points at an override, and the words
        // underneath it never went away.
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

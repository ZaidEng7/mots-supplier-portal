using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record ConfirmMfaRequest(string Code);

public sealed class ConfirmMfaRequestValidator : AbstractValidator<ConfirmMfaRequest>
{
    public ConfirmMfaRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}

public static class MfaEndpoints
{
    public static void MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/mfa").WithTags("Mfa").RequireAuthorization();

        // STORY-01.5.1: MFA is available-but-not-mandatory (ASM-081). The whole surface is gated
        // behind Mfa:Enabled so it can be toggled without a deploy.
        group.MapPost("/enroll", async (
            IScopeContext scope,
            IEnrollMfaHandler handler,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!configuration.GetValue("Mfa:Enabled", true))
            {
                return Results.NotFound(new { error = "mfa_disabled" });
            }

            if (scope.UserId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new EnrollMfaCommand(scope.UserId.Value), ct);
            return Results.Ok(new { sharedKey = result.SharedKey, authenticatorUri = result.AuthenticatorUri });
        })
        .WithName("EnrollMfa");

        group.MapPost("/confirm", async (
            ConfirmMfaRequest request,
            IValidator<ConfirmMfaRequest> validator,
            IScopeContext scope,
            IConfirmMfaEnrollmentHandler handler,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!configuration.GetValue("Mfa:Enabled", true))
            {
                return Results.NotFound(new { error = "mfa_disabled" });
            }

            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return ValidationProblems.From(validation);
            }

            if (scope.UserId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new ConfirmMfaEnrollmentCommand(scope.UserId.Value, request.Code), ct);

            return result switch
            {
                ConfirmMfaEnrollmentResult.Success s => Results.Ok(new { enrolled = true, recoveryCodes = s.RecoveryCodes }),
                ConfirmMfaEnrollmentResult.InvalidCode => Results.BadRequest(new { error = "invalid_code" }),
                _ => Results.Problem(),
            };
        })
        .WithName("ConfirmMfaEnrollment");
    }
}

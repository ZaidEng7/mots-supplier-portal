using FluentValidation;
using MotsSupplierPortal.Application.Registrations;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RegisterSupplierRequest(
    string DisplayNameAr,
    string DisplayNameEn,
    string? RegistrationNumber,
    string RepresentativeName,
    string RepresentativePhone,
    string Email,
    string Password);

public sealed class RegisterSupplierRequestValidator : AbstractValidator<RegisterSupplierRequest>
{
    public RegisterSupplierRequestValidator()
    {
        RuleFor(x => x.DisplayNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayNameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RepresentativeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RepresentativePhone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed record VerifyEmailRequest(string UserId, string Token);

public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/registrations").WithTags("Registrations").RequireRateLimiting("auth-strict");

        group.MapPost("/", async (
            RegisterSupplierRequest request,
            IValidator<RegisterSupplierRequest> validator,
            IRegisterSupplierHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var result = await handler.HandleAsync(
                new RegisterSupplierCommand(
                    request.DisplayNameAr,
                    request.DisplayNameEn,
                    request.RegistrationNumber,
                    request.RepresentativeName,
                    request.RepresentativePhone,
                    request.Email,
                    request.Password),
                ct);

            return result switch
            {
                RegisterSupplierResult.Success s => Results.Created($"/api/v1/suppliers/{s.SupplierReferenceCode}", new { referenceCode = s.SupplierReferenceCode }),
                RegisterSupplierResult.DuplicateEmail => Results.Conflict(new { error = "duplicate_email" }),
                RegisterSupplierResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithName("RegisterSupplier");

        group.MapPost("/verify", async (
            VerifyEmailRequest request,
            IVerifyEmailHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new VerifyEmailCommand(request.UserId, request.Token), ct);

            return result switch
            {
                VerifyEmailResult.Success => Results.Ok(new { verified = true }),
                VerifyEmailResult.InvalidOrExpiredToken => Results.BadRequest(new { error = "invalid_or_expired_token" }),
                _ => Results.Problem(),
            };
        })
        .WithName("VerifyEmail");
    }
}

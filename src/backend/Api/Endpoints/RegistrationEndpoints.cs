using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
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

public sealed record VerifyEmailRequest(string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        // Public by design: this is the unauthenticated front door (STORY-02.1.1). Declared
        // explicitly so the deny-by-default FallbackPolicy (MSP-67) does not silently close it,
        // and so the intent is visible rather than inferred from a missing guard.
        var group = app.MapGroup("/api/v1/registrations").WithTags("Registrations").RequireRateLimiting("auth-strict").AllowAnonymous();

        group.MapPost("/", async (
            RegisterSupplierRequest request,
            IValidator<RegisterSupplierRequest> validator,
            IRegisterSupplierHandler handler,
            HttpContext httpContext,
            PerTargetRateLimiter perTargetRateLimiter,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            // Per-target on top of the group's per-IP "auth-strict" policy (SECURITY-ARCHITECTURE
            // §5.1) - a distributed-IP attacker spamming one target email is still throttled.
            if (!perTargetRateLimiter.TryAcquire("register", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
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

            // MSP-73: Success, DuplicateEmail, and DuplicateRegistrationNumber all return the
            // identical response - same status, same body shape - so a caller cannot learn
            // whether an email or registration number was already taken. WeakPassword stays a
            // distinct 400: it is a property of the SUBMITTED password, true or false for any
            // email including ones that will never exist, so it leaks nothing about the target.
            // The existing account (not the submitter) is notified directly on either duplicate -
            // see RegisterSupplierHandler's NotifyExistingSupplierAsync.
            return result switch
            {
                RegisterSupplierResult.Success s => Results.Ok(new { message = "registration_received", referenceCode = s.SupplierReferenceCode }),
                RegisterSupplierResult.DuplicateEmail => Results.Ok(new { message = "registration_received", referenceCode = (string?)null }),
                RegisterSupplierResult.DuplicateRegistrationNumber => Results.Ok(new { message = "registration_received", referenceCode = (string?)null }),
                RegisterSupplierResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithName("RegisterSupplier")
        // NFR-SEC-009: overrides the group's "auth-strict" for this route specifically - tighter
        // than login/verify/resend-verification because a registration attempt is more
        // consequential (writes rows, sends email). See Program.cs's RegisterRateLimitPolicy.
        .RequireRateLimiting("register-strict");

        group.MapPost("/verify", async (
            VerifyEmailRequest request,
            IVerifyEmailHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new VerifyEmailCommand(request.Token), ct);

            return result switch
            {
                VerifyEmailResult.Success => Results.Ok(new { verified = true }),
                VerifyEmailResult.InvalidOrExpiredToken => Results.BadRequest(new { error = "invalid_or_expired_token" }),
                _ => Results.Problem(),
            };
        })
        .WithName("VerifyEmail");

        // STORY-02.2.1 AC3: resend is rate-limited per-IP (group policy above) + per-target.
        group.MapPost("/resend-verification", async (
            ResendVerificationRequest request,
            IValidator<ResendVerificationRequest> validator,
            IResendVerificationHandler handler,
            HttpContext httpContext,
            PerTargetRateLimiter perTargetRateLimiter,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            if (!perTargetRateLimiter.TryAcquire("resend-verification", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            await handler.HandleAsync(new ResendVerificationCommand(request.Email), ct);
            return Results.Ok(new { message = "if_account_exists_email_sent" });
        })
        .WithName("ResendVerification");
    }
}

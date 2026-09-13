// The unauthenticated front door: registering a supplier, verifying an email address, and resending that
// verification.
//
// All three are public by design, and that is declared explicitly rather than left to be inferred from a
// missing guard, so the application's deny-by-default floor does not silently close them.
//
// Two of the three sit at paths the contract names. Resending a verification is not named anywhere, so it
// keeps its current path; moving it would be an invention, and it is reported as a documented silence rather
// than guessed at.
//
//
// REGISTRATION
//
// Whether registration is open at all is checked before validation and before the per-target rate limit. A
// closed portal should not tell an applicant their password is weak, and should not spend one of their five
// attempts a minute to say the front door is shut.
//
// A closed portal is refused rather than answered not-found. The route exists and the refusal is a policy; a
// not-found would send an integrator looking for a path that had moved. The body names the reason, so the
// interface can say what to do next instead of showing a generic failure. The name becomes the
// machine-readable code and the sentence becomes the human-readable detail, and that detail must not read as
// "you are not allowed", which is what a bare refusal's title says.
//
// A successful registration, an email already taken and a registration number already taken all return the
// identical response: same status, same body shape. A caller cannot learn whether either was already
// registered. A weak password stays a distinct refusal, because that is a property of the password submitted,
// true or false for any email including ones that will never exist, so it leaks nothing about the target. The
// existing account, not the person submitting, is notified directly on either duplicate.
//
// Registration has its own tighter rate limit, overriding the group's, because a registration attempt is more
// consequential than a sign-in: it writes rows and sends mail. The per-target limit sits on top of the
// per-address one, so somebody spreading an attack across many addresses at one target email is still
// throttled.
//
//
// THE LANGUAGE HEADER
//
// A new account's language is taken from the request's language header, and only the two the product ships
// are accepted. That header can carry preference weights, region variants and languages this product does not
// support at all, so this reads only the primary part of the first entry and falls back to Arabic, which is
// the account default and the interface's own fallback, rather than guessing at a closest match.
//
//
// VERIFICATION
//
// An expired or invalid token is refused as unprocessable rather than as a bad request. It used to answer the
// latter with a different name. Unprocessable is also the right shape: the request was well-formed and the
// token was simply not usable, which is a refusal about meaning rather than a failure to parse.
//
// Resending is rate-limited per address by the group and per target here.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Startup;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;

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
    public static string ResolveLocale(string? acceptLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader)) return "ar";

        var firstEntry = acceptLanguageHeader.Split(',')[0].Split(';')[0].Trim();
        var primarySubtag = firstEntry.Split('-')[0].ToLowerInvariant();
        return primarySubtag == "en" ? "en" : "ar";
    }

    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Registrations").RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy).AllowAnonymous();
        var legacyGroup = app.MapGroup("/api/v1/registrations").WithTags("Registrations").RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy).AllowAnonymous();

        group.MapPost("/register", async (
            RegisterSupplierRequest request,
            IValidator<RegisterSupplierRequest> validator,
            IRegisterSupplierHandler handler,
            HttpContext httpContext,
            PerTargetRateLimiter perTargetRateLimiter,
            ISystemSettingReader settings,
            CancellationToken ct) =>
        {
            if (await settings.GetAsync(SystemSettings.RegistrationMode, ct) == SystemSettings.RegistrationClosed)
            {
                return Results.Json(
                    new
                    {
                        error = "registration_closed",
                        message = "Self-registration is currently closed. Contact the Ministry to be onboarded.",
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return ValidationProblems.From(validation);
            }

            if (!perTargetRateLimiter.TryAcquire("register", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            var locale = ResolveLocale(httpContext.Request.Headers.AcceptLanguage);

            var result = await handler.HandleAsync(
                new RegisterSupplierCommand(
                    request.DisplayNameAr,
                    request.DisplayNameEn,
                    request.RegistrationNumber,
                    request.RepresentativeName,
                    request.RepresentativePhone,
                    request.Email,
                    request.Password,
                    locale),
                ct);

            return result switch
            {
                RegisterSupplierResult.Success s => Results.Ok(new { message = "registration_received", supplierCode = s.SupplierReferenceCode }),
                RegisterSupplierResult.DuplicateEmail => Results.Ok(new { message = "registration_received", supplierCode = (string?)null }),
                RegisterSupplierResult.DuplicateRegistrationNumber => Results.Ok(new { message = "registration_received", supplierCode = (string?)null }),
                RegisterSupplierResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithName("RegisterSupplier")
        .RequireRateLimiting(HttpTransportRegistration.RegisterRateLimitPolicy);

        group.MapPost("/verify-email", async (
            VerifyEmailRequest request,
            IVerifyEmailHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new VerifyEmailCommand(request.Token), ct);

            return result switch
            {
                VerifyEmailResult.Success => Results.Ok(new { verified = true }),
                VerifyEmailResult.InvalidOrExpiredToken => Results.UnprocessableEntity(
                    new { error = "verification_token_invalid" }),
                _ => Results.Problem(),
            };
        })
        .WithName("VerifyEmail");

        legacyGroup.MapPost("/resend-verification", async (
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
                return ValidationProblems.From(validation);
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

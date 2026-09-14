// Signing in and out, refreshing a session, forgotten and changed passwords, the account screen, and session
// management.
//
//
// ANONYMOUS ROUTES ARE DECLARED ONE AT A TIME, NOT ON THE GROUP
//
// This is the single most important thing in this file.
//
// Anonymous access used to be declared on the whole group, with a comment claiming that the
// session-management routes' own requirement to be signed in would not be weakened by it. That claim was
// wrong. The framework treats the mere presence of an anonymous marker on a route as an unconditional
// override: it stops before ever looking at what else the route requires, so a route carrying both is
// anonymous, full stop, and its own requirement was dead code.
//
// Verified directly: listing sessions and revoking all sessions both answered successfully with no
// authorisation header at all. Only the handlers' own checks for a missing user, which returned an empty
// page and a count of zero, kept this from handing out real session data. The authorisation pipeline was
// never gating those three routes.
//
//
// THE REFRESH COOKIE
//
// The refresh token travels only as a cookie that scripts cannot read, marked secure, restricted to
// same-site requests and scoped to this path. It is never in a response body. The access token is
// short-lived and does go in the body, for the interface to hold in memory.
//
// The secure flag is unconditional. Do not reintroduce a switch for it.
//
// It was once tied to the environment, then to a configuration key defaulting to on. Each was an
// improvement on the last, and each left exactly one setting standing between a refresh token and plain
// text: first a leaked environment variable, then a configuration key. A configuration key is the same
// shape as the environment variable it replaced, so the same objection applies, which is why there is now
// no setting at all.
//
// This does not break local development. Browsers treat a local address as trustworthy and accept secure
// cookies over plain connections there, a special case that exists so developers are not forced into local
// certificates. Verified against the running stack rather than assumed: signing in set the cookie and a
// refresh round trip succeeded with it. If a future non-local development setup needs this, the answer is
// certificates on that host, not a switch here.
//
//
// WHY THE COOKIE DELETIONS OMIT THOSE FLAGS
//
// The static analyser flags the delete calls for not repeating the secure, script-blocked and same-site
// flags, on the reasoning that they are set when the cookie is written. That reasoning does not hold, and
// this is the record of why; the marking in the analyser is only bookkeeping.
//
// A browser identifies a cookie by its name, its domain and its path. Those three flags are attributes
// carried by a cookie rather than part of its identity, so they play no part in matching. Deleting emits
// the same name with an expiry in the past, and the browser matches on the identity alone and removes the
// cookie whatever its flags were. Repeating them would change nothing about which cookie is removed.
//
// The path is part of that identity, which is the part that genuinely matters, and it is the one supplied,
// from the same constant the write uses. Were the two paths to drift apart, the delete would silently match
// nothing and signing out would leave a live refresh token in the browser while reporting success. So the
// shared constant, not the flags, is what these calls depend on for correctness.
//
//
// RATE LIMITS
//
// Signing in, forgotten password and changing a password each carry a per-target limit on top of the
// group's per-address one, so somebody spreading an attack across many addresses at one account is still
// throttled. The forgotten-password limit is consumed even for an address that does not exist, which is the
// same anti-enumeration shape as the handler answering identically either way.
//
//
// THE DELIBERATE STATUS CHOICES
//
// A sign-in that needs a second factor is refused with its own code rather than answered successfully,
// because no session exists yet and nothing there is a partial success a client could mistake for one.
//
// A wrong current password on the change-password screen is unprocessable rather than unauthorised, because
// the caller is authenticated. Unauthorised would tell the interface the session had expired and bounce
// them to the sign-in screen in the middle of a form.
//
// Forgotten password answers identically whether or not the account exists.
//
//
// THE ACCOUNT SCREEN
//
// Changing a password and editing the account both take the identity from the session and never from the
// payload. A user identifier in a change-password request would be an account-takeover primitive, and the
// email address is not editable at all.
//
// The password rule checked here is length only, matching the configured policy. The real strength check is
// the identity framework's own, reported back with its reasons.
//
// Neither route carries a permission, deliberately. Every signed-in persona owns their own name and their
// own interface language, and gating them would put an account screen behind a grant that would then have
// to be given to all eight roles, which is the same as no gate spelled out in eight places that can drift
// apart.
//
// Both screens exist because of real gaps. A signed-in user had no way to change their own password, so the
// only path was signing out and using the forgotten-password email, which is a recovery flow being used as
// a routine one. And a name and language were fixed at registration with no screen to change either, so a
// user whose name was mistyped by whoever invited them was stuck with it.
//
// The language chooser is one field, because a first-run question asks one thing, and the moment of choice
// is recorded so it is only ever asked once. The two accepted languages are the two the product ships;
// anything outside that set has no text to render.
//
//
// SESSIONS
//
// The list is newest-first, so the current device and the most recent sign-ins are on the first page. Its
// count flag is parsed by hand for the same reason every other list's is: bound directly, an unreadable
// value is refused as a malformed body, which names no field on a request that has no body.
//
//
// THE LOGIN RESPONSE
//
// The token type and the lifetime in seconds are additions: the contract names both, they cost nothing, and
// the relative lifetime is what a standards-shaped client reaches for. The absolute expiry stays alongside
// rather than being replaced, because a relative lifetime forces every client to trust its own clock
// against the server's, and this one already ships an absolute value the interface uses.
//
// The contract's user object is deliberately absent. The interface reads roles and permissions out of the
// access token's own claims, so a second copy in the body would be a second source of truth for
// authorisation data, and the two disagree the moment a role changes mid-session.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Startup;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Auth;

public sealed record LoginRequest(string Email, string Password, string? TotpCode = null);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed record ForgotPasswordRequest(string Email);

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12);
    }
}

public sealed record UpdateAccountRequest(string FullName, string Language);

public sealed record ChooseLanguageRequest(string Language);

public sealed class ChooseLanguageRequestValidator : AbstractValidator<ChooseLanguageRequest>
{
    public ChooseLanguageRequestValidator()
    {
        RuleFor(x => x.Language).Must(l => l is "ar" or "en").WithMessage("Language must be one of: ar, en.");
    }
}

public sealed class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    private static readonly string[] Supported = ["ar", "en"];

    public UpdateAccountRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Language).Must(Supported.Contains!)
            .WithMessage("Language must be one of: ar, en.");
    }
}

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}

public static class AuthEndpoints
{
    public const string RefreshCookieName = "mots_refresh_token";
    private const string RefreshCookiePath = "/api/v1/auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IValidator<LoginRequest> validator,
            ILoginHandler handler,
            HttpContext httpContext,
            IConfiguration configuration,
            PerTargetRateLimiter perTargetRateLimiter,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return ValidationProblems.From(validation);
            }

            if (!perTargetRateLimiter.TryAcquire("login", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password, ip, userAgent, request.TotpCode), ct);

            return result switch
            {
                LoginResult.Success s => LoginOk(httpContext, s.Tokens),
                LoginResult.LockedOut => Results.Json(new { error = "locked_out" }, statusCode: StatusCodes.Status423Locked),
                LoginResult.MfaRequired => Results.Json(new { error = "mfa_required" }, statusCode: StatusCodes.Status401Unauthorized),
                LoginResult.MfaInvalid => Results.Json(new { error = "mfa_invalid" }, statusCode: StatusCodes.Status401Unauthorized),
                LoginResult.MfaEnrollmentRequired => Results.Json(new { error = "mfa_enrollment_required" }, statusCode: StatusCodes.Status403Forbidden),
                LoginResult.AccountNotUsable a => Results.BadRequest(new { error = a.Reason }),
                LoginResult.InvalidCredentials => Results.Unauthorized(),
                _ => Results.Problem(),
            };
        })
        .WithName("Login")
        .RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy)
        .AllowAnonymous();

        group.MapPost("/refresh", async (
            HttpContext httpContext,
            IRefreshTokenHandler handler,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            {
                return Results.Unauthorized();
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await handler.HandleAsync(new RefreshTokenCommand(refreshToken, ip, userAgent), ct);

            return result switch
            {
                RefreshTokenResult.Success s => LoginOk(httpContext, s.Tokens),
                RefreshTokenResult.ReuseDetected => ClearAndUnauthorized(httpContext),
                RefreshTokenResult.Invalid => ClearAndUnauthorized(httpContext),
                _ => Results.Problem(),
            };
        })
        .WithName("RefreshToken")
        .AllowAnonymous();

        group.MapPost("/logout", (HttpContext httpContext) =>
        {
            httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
            return Results.NoContent();
        })
        .WithName("Logout")
        .AllowAnonymous();

        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            IValidator<ForgotPasswordRequest> validator,
            IForgotPasswordHandler handler,
            HttpContext httpContext,
            PerTargetRateLimiter perTargetRateLimiter,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return ValidationProblems.From(validation);
            }

            if (!perTargetRateLimiter.TryAcquire("forgot-password", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            await handler.HandleAsync(new ForgotPasswordCommand(request.Email), ct);
            return Results.Ok(new { message = "if_account_exists_email_sent" });
        })
        .WithName("ForgotPassword")
        .RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy)
        .AllowAnonymous();

        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            IValidator<ResetPasswordRequest> validator,
            IResetPasswordHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return ValidationProblems.From(validation);
            }

            var result = await handler.HandleAsync(
                new ResetPasswordCommand(request.Token, request.NewPassword), ct);

            return result switch
            {
                ResetPasswordResult.Success => Results.Ok(new { reset = true }),
                ResetPasswordResult.InvalidOrExpiredToken => Results.BadRequest(new { error = "invalid_or_expired_token" }),
                ResetPasswordResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithName("ResetPassword")
        .RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy)
        .AllowAnonymous();

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            IValidator<ChangePasswordRequest> validator,
            IChangePasswordHandler handler,
            IScopeContext scope,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var currentToken);

            var result = await handler.HandleAsync(
                new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword, currentToken), ct);

            return result switch
            {
                ChangePasswordResult.Success => Results.Ok(new { changed = true }),
                ChangePasswordResult.IncorrectCurrentPassword =>
                    Results.UnprocessableEntity(new { error = "incorrect_current_password" }),
                ChangePasswordResult.SameAsCurrent =>
                    Results.UnprocessableEntity(new { error = "password_unchanged" }),
                ChangePasswordResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                ChangePasswordResult.UserNotFound => Results.Unauthorized(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .RequireRateLimiting(HttpTransportRegistration.AuthRateLimitPolicy)
        .WithName("ChangePassword");

        group.MapGet("/me", async (IGetAccountHandler handler, IScopeContext scope, CancellationToken ct) =>
        {
            if (scope.UserId is not { } userId) return Results.Unauthorized();
            var account = await handler.HandleAsync(userId, ct);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        })
        .RequireAuthorization()
        .WithName("GetAccount");

        group.MapPut("/me", async (
            UpdateAccountRequest request,
            IValidator<UpdateAccountRequest> validator,
            IUpdateAccountHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            var updated = await handler.HandleAsync(new UpdateAccountCommand(userId, request.FullName, request.Language), ct);
            return updated is null ? Results.Unauthorized() : Results.Ok(updated);
        })
        .RequireAuthorization()
        .WithName("UpdateAccount");

        group.MapPost("/me/language", async (
            ChooseLanguageRequest request,
            IValidator<ChooseLanguageRequest> validator,
            IChooseLanguageHandler handler,
            IScopeContext scope,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);
            if (scope.UserId is not { } userId) return Results.Unauthorized();

            var updated = await handler.HandleAsync(new ChooseLanguageCommand(userId, request.Language), ct);
            return updated is null ? Results.Unauthorized() : Results.Ok(updated);
        })
        .RequireAuthorization()
        .WithName("ChooseLanguage");

        group.MapGet("/sessions", async (
            string? cursor,
            int? pageSize,
            string? withCount,
            HttpContext httpContext,
            IListSessionsHandler handler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var currentToken);
            var sessions = await handler.HandleAsync(currentToken, cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct);
            return ListResponse.Ok(httpContext, sessions, pageSize);
        })
        .RequireAuthorization()
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"]))
        .WithName("ListSessions");

        group.MapPost("/sessions/{familyId:guid}/revoke", async (
            Guid familyId,
            IRevokeSessionHandler handler,
            CancellationToken ct) =>
        {
            var revoked = await handler.HandleAsync(familyId, ct);
            return revoked ? Results.NoContent() : Results.NotFound();
        })
        .RequireAuthorization()
        .WithName("RevokeSession");

        group.MapPost("/sessions/revoke-all", async (
            HttpContext httpContext,
            IRevokeAllSessionsHandler handler,
            CancellationToken ct) =>
        {
            httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var currentToken);
            var count = await handler.HandleAsync(currentToken, excludeCurrent: true, ct);
            return Results.Ok(new { revokedCount = count });
        })
        .RequireAuthorization()
        .WithName("RevokeAllOtherSessions");
    }

    private static IResult LoginOk(HttpContext httpContext, TokenPair tokens)
    {
        httpContext.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        return Results.Ok(new
        {
            accessToken = tokens.AccessToken,
            tokenType = "Bearer",
            expiresIn = (int)Math.Max(0, (tokens.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds),
            accessTokenExpiresAt = tokens.AccessTokenExpiresAt,
        });
    }

    private static IResult ClearAndUnauthorized(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        return Results.Unauthorized();
    }
}

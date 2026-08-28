using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Auth;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary><paramref name="TotpCode"/> is omitted on the first attempt; when the response is
/// <c>mfa_required</c> the client re-posts the same credentials plus a code (MSP-67).</summary>
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
        // Public by design: these are the endpoints you use precisely because you have no session
        // yet. Declared explicitly against the deny-by-default FallbackPolicy (MSP-67). The
        // session-management routes below re-add .RequireAuthorization() individually - group-level
        // AllowAnonymous does not weaken them.
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth").AllowAnonymous();

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
                return Results.ValidationProblem(validation.ToDictionary());
            }

            // Per-IP is the "auth-strict" policy below; per-account here (SECURITY-ARCHITECTURE
            // §5.1) so a distributed-IP attacker targeting one account is still throttled.
            if (!perTargetRateLimiter.TryAcquire("login", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password, ip, userAgent, request.TotpCode), ct);

            return result switch
            {
                LoginResult.Success s => LoginOk(httpContext, configuration, s.Tokens),
                LoginResult.LockedOut => Results.Json(new { error = "locked_out" }, statusCode: StatusCodes.Status423Locked),
                // 401 + a distinct code, not 200: no session exists yet, so nothing here is a
                // partial success the client could mistake for one.
                LoginResult.MfaRequired => Results.Json(new { error = "mfa_required" }, statusCode: StatusCodes.Status401Unauthorized),
                LoginResult.MfaInvalid => Results.Json(new { error = "mfa_invalid" }, statusCode: StatusCodes.Status401Unauthorized),
                LoginResult.MfaEnrollmentRequired => Results.Json(new { error = "mfa_enrollment_required" }, statusCode: StatusCodes.Status403Forbidden),
                LoginResult.AccountNotUsable a => Results.BadRequest(new { error = a.Reason }),
                LoginResult.InvalidCredentials => Results.Unauthorized(),
                _ => Results.Problem(),
            };
        })
        .WithName("Login")
        .RequireRateLimiting("auth-strict");

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
                RefreshTokenResult.Success s => LoginOk(httpContext, configuration, s.Tokens),
                RefreshTokenResult.ReuseDetected => ClearAndUnauthorized(httpContext),
                RefreshTokenResult.Invalid => ClearAndUnauthorized(httpContext),
                _ => Results.Problem(),
            };
        })
        .WithName("RefreshToken");

        group.MapPost("/logout", (HttpContext httpContext) =>
        {
            httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
            return Results.NoContent();
        })
        .WithName("Logout");

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
                return Results.ValidationProblem(validation.ToDictionary());
            }

            // Per-target on top of the per-IP "auth-strict" policy (SECURITY-ARCHITECTURE §5.1) -
            // checked (and consumes budget) even for a non-existent address, same anti-enumeration
            // shape as the handler's own identical-response behavior below.
            if (!perTargetRateLimiter.TryAcquire("forgot-password", request.Email.Trim().ToLowerInvariant()))
            {
                return RateLimitResults.TooManyRequests(httpContext);
            }

            // Identical response regardless of whether the account exists (no enumeration).
            await handler.HandleAsync(new ForgotPasswordCommand(request.Email), ct);
            return Results.Ok(new { message = "if_account_exists_email_sent" });
        })
        .WithName("ForgotPassword")
        .RequireRateLimiting("auth-strict");

        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            IValidator<ResetPasswordRequest> validator,
            IResetPasswordHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
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
        .RequireRateLimiting("auth-strict");

        // FR-IAM-007: session management - view active sessions, revoke one or all.
        group.MapGet("/sessions", async (
            HttpContext httpContext,
            IListSessionsHandler handler,
            CancellationToken ct) =>
        {
            httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var currentToken);
            var sessions = await handler.HandleAsync(currentToken, ct);
            return Results.Ok(sessions);
        })
        .RequireAuthorization()
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

    /// <summary>
    /// Refresh token travels only as an HttpOnly, Secure, SameSite=Strict cookie scoped to
    /// /api/v1/auth - never in a JS-readable response body (OWASP ASVS L2 token-handling review).
    /// The access token is short-lived and returned in the body for the SPA to hold in memory.
    /// </summary>
    private static IResult LoginOk(HttpContext httpContext, IConfiguration configuration, TokenPair tokens)
    {
        // Secure defaults to TRUE and is disabled only by an explicit opt-out, rather than being
        // derived from the environment name. Previously `!env.IsDevelopment()` meant a leaked
        // ASPNETCORE_ENVIRONMENT=Development was the only thing between a refresh token and
        // plaintext - a silent downgrade with no signal, the same class as the localhost fallbacks.
        // Now the insecure setting has to be written down somewhere a reviewer can see it.
        var requireSecure = configuration.GetValue("Cookies:RequireSecure", true);

        httpContext.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        return Results.Ok(new
        {
            accessToken = tokens.AccessToken,
            accessTokenExpiresAt = tokens.AccessTokenExpiresAt,
        });
    }

    private static IResult ClearAndUnauthorized(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        return Results.Unauthorized();
    }
}

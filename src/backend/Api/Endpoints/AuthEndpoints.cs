using MotsSupplierPortal.Api.Errors;
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
        // Task #11: AllowAnonymous is applied per-route below, NOT at the group level. It used to
        // be here, with a comment claiming the session-management routes' own .RequireAuthorization()
        // "does not weaken" the group's AllowAnonymous - that claim was wrong. ASP.NET Core's
        // AuthorizationMiddleware treats the mere PRESENCE of IAllowAnonymous metadata on an endpoint
        // as an unconditional override: it short-circuits before ever looking at IAuthorizeData, so
        // an endpoint carrying BOTH (inherited AllowAnonymous from the group, plus its own
        // RequireAuthorization) is anonymous, full stop - the RequireAuthorization call was dead code.
        // Verified directly: GET /sessions and POST /sessions/revoke-all both returned 200 with no
        // Authorization header at all; only the handlers' own `scope.UserId is null` guards (which
        // return an empty page / revokedCount 0) kept this from leaking real session data - the auth
        // pipeline itself was never actually gating these three routes.
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
                LoginResult.Success s => LoginOk(httpContext, s.Tokens),
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
        .RequireRateLimiting("auth-strict")
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
            // Sonar flags these Delete calls for omitting Secure/HttpOnly/SameSite, on the reasoning
            // that they are set on the Append above. That reasoning does not hold, and this comment
            // is the record of why - the marking in SonarCloud is only bookkeeping.
            //
            // A browser identifies a cookie by the triple (name, domain, path). Secure, HttpOnly and
            // SameSite are attributes carried BY a cookie, not part of its identity, so they play no
            // role in matching. Delete emits a Set-Cookie for the same name with an expiry in the
            // past; the browser matches it on the triple alone and removes the cookie whatever its
            // flags were. Repeating Secure/HttpOnly here would change nothing about which cookie is
            // removed.
            //
            // Path IS part of that triple, which is the part that genuinely matters, and it is the
            // one supplied: RefreshCookiePath is the same constant the Append uses. Were the paths
            // to drift apart, the Delete would silently match nothing and logout would leave a live
            // refresh token in the browser while reporting 204 - so the shared constant, not the
            // flags, is what this call depends on for correctness.
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
        .RequireRateLimiting("auth-strict")
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
        .RequireRateLimiting("auth-strict")
        .AllowAnonymous();

        // FR-IAM-007: session management - view active sessions, revoke one or all.
        group.MapGet("/sessions", async (
            string? cursor,
            int? pageSize,
            string? withCount,
            HttpContext httpContext,
            IListSessionsHandler handler,
            CancellationToken ct) =>
        {
            // `withCount` binds to `bool?`, so an unparseable value is refused by model binding with
            // a 400 MALFORMED_JSON - the wrong code for an unprocessable filter value on a GET with
            // no body, and one that names no field. Parsed as text so the refusal is the same
            // 422/INVALID_FILTER_VALUE every other filter value in this API earns.
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var currentToken);
            var sessions = await handler.HandleAsync(currentToken, cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct);
            return ListResponse.Ok(httpContext, sessions, pageSize);
        })
        .RequireAuthorization()
        // Newest session first, so "this device" and the most recent sign-ins are on page one.
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

    /// <summary>
    /// Refresh token travels only as an HttpOnly, Secure, SameSite=Strict cookie scoped to
    /// /api/v1/auth - never in a JS-readable response body (OWASP ASVS L2 token-handling review).
    /// The access token is short-lived and returned in the body for the SPA to hold in memory.
    /// </summary>
    private static IResult LoginOk(HttpContext httpContext, TokenPair tokens)
    {
        httpContext.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            // Secure is UNCONDITIONAL. Do not reintroduce a toggle here.
            //
            // This was `!env.IsDevelopment()`, then `Cookies:RequireSecure` defaulting to true.
            // Both were an improvement on the last, and both left one setting standing between a
            // refresh token and plaintext: first a leaked ASPNETCORE_ENVIRONMENT, then a config key.
            // A config key is the same shape as the environment variable it replaced, so the same
            // objection applies - which is why there is now no setting at all.
            //
            // This does not break local development. Browsers treat http://localhost as a
            // trustworthy origin and accept Secure cookies over plain HTTP there (the "secure
            // contexts" special case exists so developers are not forced into local TLS). Verified
            // against the running stack over http://localhost, not assumed: login set the cookie
            // and a subsequent refresh round-trip succeeded with it.
            //
            // If a future non-localhost dev setup needs this, the answer is TLS on that host, not a
            // switch here. csharpsquid:S2092 flagged the previous conditional form; it is satisfied
            // now because the value is genuinely constant rather than annotated away.
            Secure = true,
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
        // Same Sonar finding and same answer as the Delete in /logout above: Secure/HttpOnly/SameSite
        // are not part of the (name, domain, path) triple a browser matches on, so omitting them
        // does not affect which cookie is removed. Path is part of it, and is supplied.
        httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        return Results.Unauthorized();
    }
}

using FluentValidation;
using MotsSupplierPortal.Application.Auth;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password);

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

public sealed record ResetPasswordRequest(string UserId, string Token, string NewPassword);

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
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
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password, ip, userAgent), ct);

            return result switch
            {
                LoginResult.Success s => LoginOk(httpContext, env, s.Tokens),
                LoginResult.LockedOut => Results.Json(new { error = "locked_out" }, statusCode: StatusCodes.Status423Locked),
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
            IWebHostEnvironment env,
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
                RefreshTokenResult.Success s => LoginOk(httpContext, env, s.Tokens),
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
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
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
                new ResetPasswordCommand(request.UserId, request.Token, request.NewPassword), ct);

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
    }

    /// <summary>
    /// Refresh token travels only as an HttpOnly, Secure, SameSite=Strict cookie scoped to
    /// /api/v1/auth - never in a JS-readable response body (OWASP ASVS L2 token-handling review).
    /// The access token is short-lived and returned in the body for the SPA to hold in memory.
    /// </summary>
    private static IResult LoginOk(HttpContext httpContext, IWebHostEnvironment env, TokenPair tokens)
    {
        httpContext.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
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

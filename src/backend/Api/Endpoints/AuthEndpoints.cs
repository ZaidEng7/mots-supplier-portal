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

public sealed record RefreshRequest(string RefreshToken);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IValidator<LoginRequest> validator,
            ILoginHandler handler,
            HttpContext httpContext,
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
                LoginResult.Success s => Results.Ok(new
                {
                    accessToken = s.Tokens.AccessToken,
                    accessTokenExpiresAt = s.Tokens.AccessTokenExpiresAt,
                    refreshToken = s.Tokens.RefreshToken,
                }),
                LoginResult.LockedOut => Results.Json(new { error = "locked_out" }, statusCode: StatusCodes.Status423Locked),
                LoginResult.AccountNotUsable a => Results.BadRequest(new { error = a.Reason }),
                LoginResult.InvalidCredentials => Results.Unauthorized(),
                _ => Results.Problem(),
            };
        })
        .WithName("Login");

        group.MapPost("/refresh", async (
            RefreshRequest request,
            IRefreshTokenHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await handler.HandleAsync(new RefreshTokenCommand(request.RefreshToken, ip, userAgent), ct);

            return result switch
            {
                RefreshTokenResult.Success s => Results.Ok(new
                {
                    accessToken = s.Tokens.AccessToken,
                    accessTokenExpiresAt = s.Tokens.AccessTokenExpiresAt,
                    refreshToken = s.Tokens.RefreshToken,
                }),
                RefreshTokenResult.ReuseDetected => Results.Unauthorized(),
                RefreshTokenResult.Invalid => Results.Unauthorized(),
                _ => Results.Problem(),
            };
        })
        .WithName("RefreshToken");
    }
}

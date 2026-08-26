namespace MotsSupplierPortal.Application.Auth;

public sealed record LoginCommand(string Email, string Password, string? Ip, string? UserAgent);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public abstract record LoginResult
{
    public sealed record Success(TokenPair Tokens) : LoginResult;
    public sealed record InvalidCredentials : LoginResult;
    public sealed record AccountNotUsable(string Reason) : LoginResult;
    public sealed record LockedOut : LoginResult;
}

public interface ILoginHandler
{
    Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken ct);
}

public sealed record RefreshTokenCommand(string RefreshToken, string? Ip, string? UserAgent);

public abstract record RefreshTokenResult
{
    public sealed record Success(TokenPair Tokens) : RefreshTokenResult;
    public sealed record Invalid : RefreshTokenResult;
    public sealed record ReuseDetected : RefreshTokenResult;
}

public interface IRefreshTokenHandler
{
    Task<RefreshTokenResult> HandleAsync(RefreshTokenCommand command, CancellationToken ct);
}

// The vocabulary for signing in and for refreshing a session.
//
// A sign-in carries no second-factor code on its first attempt. When the account has one enabled, the answer
// says so and the client submits the same credentials again together with a code.
//
// Four things can go wrong, and they are separate outcomes because the screen says something different about
// each.
//
// A second factor is needed, which carries no session at all.
//
// A second factor was supplied and was wrong, whether that was a code from an authenticator or a recovery
// code.
//
// The account's role requires a second factor and none is enrolled yet, so enrolment has to be completed
// before any session is issued.
//
// And the ordinary refusal, which says nothing about which half was wrong.

namespace MotsSupplierPortal.Application.Auth;

public sealed record LoginCommand(string Email, string Password, string? Ip, string? UserAgent, string? TotpCode = null);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public abstract record LoginResult
{
    public sealed record Success(TokenPair Tokens) : LoginResult;
    public sealed record InvalidCredentials : LoginResult;
    public sealed record AccountNotUsable(string Reason) : LoginResult;
    public sealed record LockedOut : LoginResult;
    public sealed record MfaRequired : LoginResult;
    public sealed record MfaInvalid : LoginResult;
    public sealed record MfaEnrollmentRequired : LoginResult;
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

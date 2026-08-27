namespace MotsSupplierPortal.Application.Auth;

/// <summary><paramref name="TotpCode"/> is null on the first leg of a login. When the account has
/// MFA enabled the handler answers <see cref="LoginResult.MfaRequired"/> and the client re-submits
/// the same credentials together with a code (MSP-67 / FR-IAM-004).</summary>
public sealed record LoginCommand(string Email, string Password, string? Ip, string? UserAgent, string? TotpCode = null);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public abstract record LoginResult
{
    public sealed record Success(TokenPair Tokens) : LoginResult;
    public sealed record InvalidCredentials : LoginResult;
    public sealed record AccountNotUsable(string Reason) : LoginResult;
    public sealed record LockedOut : LoginResult;
    /// <summary>Password was correct but a second factor is needed. Carries no session.</summary>
    public sealed record MfaRequired : LoginResult;
    /// <summary>Second factor supplied but wrong (TOTP or recovery code).</summary>
    public sealed record MfaInvalid : LoginResult;
    /// <summary>NFR-SEC-003: the account's role mandates MFA and it is not enrolled yet. The user
    /// must complete enrolment before a session is issued.</summary>
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

namespace MotsSupplierPortal.Application.Auth;

public sealed record ForgotPasswordCommand(string Email);

public interface IForgotPasswordHandler
{
    /// <summary>
    /// Always succeeds from the caller's point of view - no user-enumeration (FR-IAM-005).
    /// If the account exists, a reset email is queued; if not, this is a silent no-op.
    /// </summary>
    Task HandleAsync(ForgotPasswordCommand command, CancellationToken ct);
}

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public abstract record ResetPasswordResult
{
    public sealed record Success : ResetPasswordResult;
    public sealed record InvalidOrExpiredToken : ResetPasswordResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : ResetPasswordResult;
}

public interface IResetPasswordHandler
{
    Task<ResetPasswordResult> HandleAsync(ResetPasswordCommand command, CancellationToken ct);
}

/// <summary>
/// SCR-903: a signed-in user changing their own password.
///
/// <para><b>Distinct from a reset, and not a special case of one.</b> A reset proves identity with a
/// token sent to an address; a change proves it with the current password, which is the only
/// evidence available to someone already holding a session. Sharing one handler would mean either
/// accepting a change with no proof, or asking a signed-in user to go and find an email.</para>
/// </summary>
/// <param name="CurrentRefreshToken">The caller's own refresh cookie, so their current session can be
/// excluded from the revocation. Passed in rather than read here for the same reason
/// <c>RevokeAllSessions</c> takes it: the handler has no HTTP context.</param>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword, string? CurrentRefreshToken);

public abstract record ChangePasswordResult
{
    public sealed record Success : ChangePasswordResult;

    /// <summary>The current password did not match. Deliberately its own case rather than folded
    /// into a generic failure: it is the one outcome the user can act on by retyping.</summary>
    public sealed record IncorrectCurrentPassword : ChangePasswordResult;

    public sealed record WeakPassword(IReadOnlyList<string> Errors) : ChangePasswordResult;

    /// <summary>The new password is the current one. Refused rather than accepted as a no-op: a
    /// change that changes nothing still revokes every other session, so silently "succeeding" would
    /// sign the user out of their other devices for no reason.</summary>
    public sealed record SameAsCurrent : ChangePasswordResult;

    public sealed record UserNotFound : ChangePasswordResult;
}

public interface IChangePasswordHandler
{
    Task<ChangePasswordResult> HandleAsync(ChangePasswordCommand command, CancellationToken ct);
}

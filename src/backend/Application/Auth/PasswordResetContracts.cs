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

public sealed record ResetPasswordCommand(string UserId, string Token, string NewPassword);

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

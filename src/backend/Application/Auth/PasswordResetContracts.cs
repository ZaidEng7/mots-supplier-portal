// The vocabulary for the three ways a password changes: forgetting one, resetting it with a token, and
// changing it while signed in.
//
// Asking for a reset always succeeds from the caller's point of view. If the account exists an email is
// queued; if not, it is a silent no-op. Otherwise the form would answer the question of who is registered.
//
// Changing a password while signed in is distinct from a reset rather than a special case of it. A reset
// proves identity with a token sent to an address. A change proves it with the current password, which is the
// only thing a signed-in session has not already demonstrated.

namespace MotsSupplierPortal.Application.Auth;

public sealed record ForgotPasswordCommand(string Email);

public interface IForgotPasswordHandler
{
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

public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword, string? CurrentRefreshToken);

public abstract record ChangePasswordResult
{
    public sealed record Success : ChangePasswordResult;

    public sealed record IncorrectCurrentPassword : ChangePasswordResult;

    public sealed record WeakPassword(IReadOnlyList<string> Errors) : ChangePasswordResult;

    public sealed record SameAsCurrent : ChangePasswordResult;

    public sealed record UserNotFound : ChangePasswordResult;
}

public interface IChangePasswordHandler
{
    Task<ChangePasswordResult> HandleAsync(ChangePasswordCommand command, CancellationToken ct);
}

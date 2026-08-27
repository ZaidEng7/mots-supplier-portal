namespace MotsSupplierPortal.Application.Registrations;

public sealed record VerifyEmailCommand(string Token);

public abstract record VerifyEmailResult
{
    public sealed record Success : VerifyEmailResult;
    public sealed record InvalidOrExpiredToken : VerifyEmailResult;
}

public interface IVerifyEmailHandler
{
    Task<VerifyEmailResult> HandleAsync(VerifyEmailCommand command, CancellationToken ct);
}

public sealed record ResendVerificationCommand(string Email);

public interface IResendVerificationHandler
{
    /// <summary>Always succeeds from the caller's perspective (STORY-02.2.1/§1.6: resend "does not
    /// reveal whether an address exists") - a non-existent or already-verified account is a silent
    /// no-op, same anti-enumeration shape as ForgotPasswordHandler.</summary>
    Task HandleAsync(ResendVerificationCommand command, CancellationToken ct);
}

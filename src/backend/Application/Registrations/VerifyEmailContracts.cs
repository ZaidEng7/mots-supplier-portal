// The vocabulary for confirming an email address, and for asking for that confirmation again.
//
// Verifying reports whether the token was usable. Resending does not report anything, and that is the
// point: it always succeeds from the caller's side, so a non-existent or already-verified address is a
// silent no-op. Otherwise the form would answer the question of whether an address is registered.
//
// That is the same shape the forgotten-password path uses, for the same reason.

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
    Task HandleAsync(ResendVerificationCommand command, CancellationToken ct);
}

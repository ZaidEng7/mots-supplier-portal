// The vocabulary for enrolling a second factor.
//
// Enrolling issues the shared key, or reuses one already issued, and the address an authenticator app reads.
//
// Confirming verifies the first code, switches the second factor on, and issues the one-time recovery codes.
// Both steps exist because a key handed out and never confirmed would leave an account believing it was
// protected.

namespace MotsSupplierPortal.Application.Auth;

public sealed record EnrollMfaCommand(Guid UserId);

public sealed record EnrollMfaResult(string SharedKey, string AuthenticatorUri);

public interface IEnrollMfaHandler
{
    Task<EnrollMfaResult> HandleAsync(EnrollMfaCommand command, CancellationToken ct);
}

public sealed record ConfirmMfaEnrollmentCommand(Guid UserId, string Code);

public abstract record ConfirmMfaEnrollmentResult
{
    public sealed record Success(IReadOnlyList<string> RecoveryCodes) : ConfirmMfaEnrollmentResult;
    public sealed record InvalidCode : ConfirmMfaEnrollmentResult;
}

public interface IConfirmMfaEnrollmentHandler
{
    Task<ConfirmMfaEnrollmentResult> HandleAsync(ConfirmMfaEnrollmentCommand command, CancellationToken ct);
}

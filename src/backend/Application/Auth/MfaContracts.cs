namespace MotsSupplierPortal.Application.Auth;

public sealed record EnrollMfaCommand(Guid UserId);

public sealed record EnrollMfaResult(string SharedKey, string AuthenticatorUri);

public interface IEnrollMfaHandler
{
    /// <summary>STORY-01.5.1: begins TOTP enrollment - issues (or reuses) the authenticator key.</summary>
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
    /// <summary>Verifies the first TOTP code, then flips 2FA on and issues one-time recovery codes.</summary>
    Task<ConfirmMfaEnrollmentResult> HandleAsync(ConfirmMfaEnrollmentCommand command, CancellationToken ct);
}

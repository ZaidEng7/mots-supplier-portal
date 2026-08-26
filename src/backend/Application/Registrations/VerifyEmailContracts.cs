namespace MotsSupplierPortal.Application.Registrations;

public sealed record VerifyEmailCommand(string UserId, string Token);

public abstract record VerifyEmailResult
{
    public sealed record Success : VerifyEmailResult;
    public sealed record InvalidOrExpiredToken : VerifyEmailResult;
}

public interface IVerifyEmailHandler
{
    Task<VerifyEmailResult> HandleAsync(VerifyEmailCommand command, CancellationToken ct);
}

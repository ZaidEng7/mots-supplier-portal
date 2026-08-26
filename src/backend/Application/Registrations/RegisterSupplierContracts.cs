namespace MotsSupplierPortal.Application.Registrations;

public sealed record RegisterSupplierCommand(
    string DisplayNameAr,
    string DisplayNameEn,
    string? RegistrationNumber,
    string RepresentativeName,
    string Email,
    string Password);

public abstract record RegisterSupplierResult
{
    public sealed record Success(string SupplierReferenceCode) : RegisterSupplierResult;
    public sealed record DuplicateEmail : RegisterSupplierResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : RegisterSupplierResult;
}

public interface IRegisterSupplierHandler
{
    Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct);
}

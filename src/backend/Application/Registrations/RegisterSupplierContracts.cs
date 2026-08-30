namespace MotsSupplierPortal.Application.Registrations;

public sealed record RegisterSupplierCommand(
    string DisplayNameAr,
    string DisplayNameEn,
    string? RegistrationNumber,
    string RepresentativeName,
    string RepresentativePhone,
    string Email,
    string Password);

public abstract record RegisterSupplierResult
{
    public sealed record Success(string SupplierReferenceCode) : RegisterSupplierResult;

    /// <summary>MSP-73: internal-only distinction now. RegistrationEndpoints.cs maps this to the
    /// same response shape as Success and DuplicateRegistrationNumber - a caller cannot tell
    /// which of the three happened, or that a duplicate was detected at all.</summary>
    public sealed record DuplicateEmail : RegisterSupplierResult;

    /// <summary>FR-REG-004. MSP-73: same non-enumerating treatment as DuplicateEmail now - mapped
    /// to the identical response shape as Success, not a distinct 409 naming which field
    /// collided. Deliberately symmetric with DuplicateEmail's fix, not just its old leak.</summary>
    public sealed record DuplicateRegistrationNumber : RegisterSupplierResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : RegisterSupplierResult;
}

public interface IRegisterSupplierHandler
{
    Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct);
}

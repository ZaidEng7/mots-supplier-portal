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
    public sealed record DuplicateEmail : RegisterSupplierResult;

    /// <summary>FR-REG-004. Mapped at the API the same way DuplicateEmail already is - a 409
    /// naming which field collided. That symmetry is deliberate, not an improvement: email
    /// dedupe today already tells a caller "this email exists" via a distinct response shape
    /// (Results.Conflict vs Results.Created), which is a live, confirmed enumeration vector. This
    /// result does not fix that; it matches the existing behaviour rather than inventing a second,
    /// differently-shaped leak. Whether either should stop naming the reason is the open
    /// enumeration-fix decision (task #17 / MSP-73), not this ticket.</summary>
    public sealed record DuplicateRegistrationNumber : RegisterSupplierResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : RegisterSupplierResult;
}

public interface IRegisterSupplierHandler
{
    Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct);
}

namespace MotsSupplierPortal.Application.Registrations;

/// <summary>MSP-69: <paramref name="Locale"/> is "ar" or "en", resolved by the endpoint from the
/// request's Accept-Language header (RegistrationEndpoints.cs) - the frontend has no in-app language
/// switcher independent of the browser (src/frontend/src/i18n/config.ts's own
/// i18next-browser-languagedetector), so the header is a faithful signal of what the registrant
/// actually saw the form rendered in, not a guess.</summary>
public sealed record RegisterSupplierCommand(
    string DisplayNameAr,
    string DisplayNameEn,
    string? RegistrationNumber,
    string RepresentativeName,
    string RepresentativePhone,
    string Email,
    string Password,
    string Locale);

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

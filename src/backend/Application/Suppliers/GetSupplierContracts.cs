namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierDto(
    string ReferenceCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string OnboardingState,
    string? RegistrationNumber,
    string? TaxId,
    string? AddressLine,
    string? City,
    string? Country,
    string? CurrencyCode,
    string? PrimaryContactPhone,
    IReadOnlyList<string> MissingProfileFields,
    string? TermsAcceptedVersion,
    DateTimeOffset? TermsAcceptedAt);

public abstract record GetSupplierResult
{
    public sealed record Found(SupplierDto Supplier) : GetSupplierResult;
    public sealed record NotFoundOrOutOfScope : GetSupplierResult;
}

public interface IGetSupplierHandler
{
    /// <summary>Row-scoped: a caller may only ever resolve their own SupplierId (STORY-01.8.1).</summary>
    Task<GetSupplierResult> HandleAsync(string referenceCode, CancellationToken ct);

    /// <summary>Resolves the caller's own supplier record from the JWT's supplierId claim only.</summary>
    Task<GetSupplierResult> HandleOwnAsync(CancellationToken ct);
}

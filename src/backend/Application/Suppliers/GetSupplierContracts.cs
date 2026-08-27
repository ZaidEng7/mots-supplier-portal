namespace MotsSupplierPortal.Application.Suppliers;

public sealed record LegalInfoDto(
    string? LegalNameAr,
    string? LegalNameEn,
    string? RegistrationNumber,
    string? TaxId,
    string? SupplierType,
    DateOnly? EstablishedOn);

public sealed record AddressDto(
    Guid Id,
    string Kind,
    string Line1,
    string? Line2,
    string City,
    string RegionCode,
    string Country,
    string? PostalCode,
    double? Latitude,
    double? Longitude,
    bool IsPrimary);

public sealed record ContactDto(Guid Id, string FullName, string Email, string? Phone, string? Role);

public sealed record RepresentativeDto(Guid Id, string FullName, string Email, string? Phone, string? Position, bool IsPrimary);

public sealed record BranchDto(Guid Id, string NameAr, string NameEn, Guid? AddressId, bool IsActive);

public sealed record BankAccountDto(
    Guid Id,
    string AccountHolderName,
    string BankName,
    string? BranchName,
    string MaskedAccountNumber,
    string? SwiftBic,
    string CurrencyCode,
    bool IsDefault);

/// <summary>Supplier-facing profile. FEAT-04.10's ERP mapping fields (ExternalId/SyncStatus/
/// LastSyncedAt) are deliberately NOT here - they're read-only to STAFF, not visible to the
/// supplier at all; see ErpSyncDto/ReviewerSupplierViewDto for the staff-facing view.</summary>
public sealed record SupplierDto(
    string ReferenceCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string? Description,
    string? Website,
    string? LogoStorageKey,
    string? SupplierGroup,
    string OnboardingState,
    string? CurrencyCode,
    LegalInfoDto? LegalInfo,
    string? PrimaryContactPhone,
    IReadOnlyList<RepresentativeDto> Representatives,
    IReadOnlyList<AddressDto> Addresses,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<BranchDto> Branches,
    IReadOnlyList<BankAccountDto> BankAccounts,
    IReadOnlyList<string> CategoryCodes,
    IReadOnlyList<string> MissingProfileFields,
    string? TermsAcceptedVersion,
    DateTimeOffset? TermsAcceptedAt,
    /// <summary>Postgres xmin-backed optimistic concurrency token - changes on every write to this
    /// supplier row. A client can compare it across two GETs to detect "someone else changed this
    /// since I last loaded it" (no client-supplied If-Match write path yet - that's a separate,
    /// larger change across every mutation endpoint, flagged but not built here).</summary>
    long RowVersion);

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

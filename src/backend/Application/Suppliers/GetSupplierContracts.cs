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
    // R-9: §12.2's names are authoritative. supplierCode, defaultCurrency and categories are pure
    // renames and are taken as written. The two §12.2 names that are NOT renames are left alone and
    // recorded instead - see the note under CategoryCodes.
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string? Description,
    string? Website,
    string? LogoStorageKey,
    string? SupplierGroup,
    string OnboardingState,
    // MSP-63: exposed so the staff UI knows which lifecycle action to offer. Additive - the SPA
    // ignores unknown fields, and no existing consumer reads it.
    string LifecycleState,
    string? DefaultCurrency,
    LegalInfoDto? LegalInfo,
    string? PrimaryContactPhone,
    IReadOnlyList<RepresentativeDto> Representatives,
    IReadOnlyList<AddressDto> Addresses,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<BranchDto> Branches,
    IReadOnlyList<BankAccountDto> BankAccounts,
    /// <summary>
    /// R-9 stops here. §12.2 also shows <c>legalName</c> and <c>displayName</c> as SINGLE values -
    /// and its own example puts Arabic in one ("شركة نور للمنسوجات") and English in the other
    /// ("Nour Linens"). This code carries <c>displayNameAr</c>/<c>displayNameEn</c> and
    /// <c>legalInfo.legalNameAr</c>/<c>legalNameEn</c>. Conforming to the document there is not a
    /// rename: it would collapse two stored values into one and delete a language from the API of
    /// an Arabic-first product. R-9 rules that the document's NAMES win; it does not rule a
    /// bilingual pair into a single value, and the backlog's own decision table already carries
    /// "bilingual fields vs the documented single-value shape" as a separate open question. Left as
    /// it is, deliberately, and reported rather than quietly conformed.
    /// </summary>
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> MissingProfileFields,
    string? TermsAcceptedVersion,
    DateTimeOffset? TermsAcceptedAt,
    /// <summary>Postgres xmin-backed optimistic concurrency token - changes on every write to this
    /// supplier row. A client can compare it across two GETs to detect "someone else changed this
    /// since I last loaded it" (no client-supplied If-Match write path yet - that's a separate,
    /// larger change across every mutation endpoint, flagged but not built here).</summary>
    long RowVersion,
    // BRULE-018 (MSP-68): required document types whose latest version is Rejected or Expired -
    // the profile is flagged incomplete until they are replaced with approved versions.
    //
    // NULL means "not computed on this response"; EMPTY means "nothing outstanding". They are
    // different answers and a client must be able to tell them apart. This DTO is returned from 32
    // call sites, nearly all profile mutations with no reason to run the query - a field silently
    // defaulting to empty there would report a clean profile on every edit.
    IReadOnlyList<string>? IncompleteDocumentTypeCodes = null,
    /// <summary>
    /// §12.2's <c>profileCompleteness</c>. Supplied on the READ path only - the same precedent
    /// <c>IncompleteDocumentTypeCodes</c> above already sets, and the only place §12.2 shows the
    /// field. Computing it needs a document query the mutation handlers have no reason to run.
    ///
    /// <para>Nullable rather than defaulted to 0: "not computed on this response" and "this supplier
    /// has done nothing" are different facts, and a zero would assert the second on every edit.</para>
    /// </summary>
    double? ProfileCompleteness = null);

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

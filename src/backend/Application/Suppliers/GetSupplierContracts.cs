// The shapes a supplier profile is read through, and everything attached to it.
//
//
// THE DOCUMENTS SUMMARY, AND WHY ITS NUMBERS DO NOT ADD UP
//
// It was in the written contract and computed by nothing.
//
// The required count is the document types this particular supplier must hold, resolved by the same function
// the submit gate and the completeness fraction ask, so the three cannot disagree about what is required.
//
// The approved, pending and rejected counts count those required types by the state of their latest version.
//
// They deliberately do not sum to the required count, and that is the useful part. A required type with nothing
// uploaded counts in none of the three, so the difference is the number of documents the supplier has not sent
// at all. A summary that forced the totals to match would have to invent a fourth state for absent, which is
// the one state a document does not have.
//
// An expiring or expired document counts as neither approved nor pending. It was approved once and is not now,
// which is precisely why it re-opens the profile.
//
//
// WHAT THE SUPPLIER'S OWN VIEW DOES NOT CARRY
//
// The finance-system mapping fields are absent. They are read-only to staff and not visible to the supplier at
// all; the staff-facing view carries them.
//
// The lifecycle state is present so a staff screen knows which action to offer. It was added without breaking
// anything, because the interface ignores fields it does not know and no existing consumer read it.
//
//
// TWO NAMES THAT DIVERGE FROM THE CONTRACT, ON PURPOSE
//
// The contract shows the legal name and the display name as single values, and its own example puts Arabic in
// one and English in the other.
//
// This carries both languages for both, as separate fields. Conforming there would mean choosing which
// language a name is, which is not a choice this product can make: the same profile is read by screens in both
// languages, and a supplier's Arabic legal name is not a translation of its English one.
//
// Recorded here rather than silently diverged from.

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

public sealed record DocumentsSummaryDto(int Required, int Approved, int Pending, int Rejected);

public sealed record SupplierDto(
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string? Description,
    string? Website,
    string? LogoStorageKey,
    string? SupplierGroup,
    string OnboardingState,
    string LifecycleState,
    string? DefaultCurrency,
    LegalInfoDto? LegalInfo,
    string? PrimaryContactPhone,
    IReadOnlyList<RepresentativeDto> Representatives,
    IReadOnlyList<AddressDto> Addresses,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<BranchDto> Branches,
    IReadOnlyList<BankAccountDto> BankAccounts,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> MissingProfileFields,
    string? TermsAcceptedVersion,
    DateTimeOffset? TermsAcceptedAt,
    long RowVersion,
    IReadOnlyList<string>? IncompleteDocumentTypeCodes = null,
    double? ProfileCompleteness = null,
    DocumentsSummaryDto? DocumentsSummary = null,
    DateTimeOffset? UpdatedAt = null,
    string? PrimaryCategoryCode = null);

public abstract record GetSupplierResult
{
    public sealed record Found(SupplierDto Supplier) : GetSupplierResult;
    public sealed record NotFoundOrOutOfScope : GetSupplierResult;
}

public interface IGetSupplierHandler
{
    Task<GetSupplierResult> HandleAsync(string referenceCode, CancellationToken ct);

    Task<GetSupplierResult> HandleOwnAsync(CancellationToken ct);
}

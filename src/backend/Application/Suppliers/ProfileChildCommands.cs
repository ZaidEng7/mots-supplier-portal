using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

// --- Representative ---
public sealed record AddRepresentativeCommand(string FullName, string Email, string? Phone, string? Position);

public sealed record UpdateRepresentativeCommand(Guid RepresentativeId, string FullName, string Email, string? Phone, string? Position);

public sealed record RemoveRepresentativeCommand(Guid RepresentativeId);

public sealed record SetPrimaryRepresentativeCommand(Guid RepresentativeId);

// --- Address ---
public sealed record AddAddressCommand(AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed record UpdateAddressCommand(Guid AddressId, AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed record RemoveAddressCommand(Guid AddressId);

// --- Contact ---
public sealed record AddContactCommand(string FullName, string Email, string? Phone, string? Role);

public sealed record UpdateContactCommand(Guid ContactId, string FullName, string Email, string? Phone, string? Role);

public sealed record RemoveContactCommand(Guid ContactId);

// --- Branch ---
public sealed record AddBranchCommand(string NameAr, string NameEn, Guid? AddressId);

public sealed record UpdateBranchCommand(Guid BranchId, string NameAr, string NameEn, Guid? AddressId, bool IsActive);

public sealed record RemoveBranchCommand(Guid BranchId);

// --- Bank account ---
public sealed record AddBankAccountCommand(string AccountHolderName, string BankName, string? BranchName, string AccountNumber, string? SwiftBic, string CurrencyCode);

/// <summary>AccountNumber is optional: pass null to leave the existing encrypted value untouched
/// (so correcting the holder name doesn't force re-entering the account number), or a new value to
/// re-encrypt/re-mask it - same as Add, never stored/logged in plaintext.</summary>
public sealed record UpdateBankAccountCommand(Guid BankAccountId, string AccountHolderName, string BankName, string? BranchName, string? AccountNumber, string? SwiftBic, string CurrencyCode);

public sealed record RemoveBankAccountCommand(Guid BankAccountId);

public sealed record SetDefaultBankAccountCommand(Guid BankAccountId);

public sealed record RevealBankAccountCommand(Guid BankAccountId);

// --- Category link ---
public sealed record LinkCategoryCommand(string CategoryCode);

public sealed record UnlinkCategoryCommand(string CategoryCode);

// What can be asked about the things attached to a supplier's profile: its representatives, addresses,
// contacts, branches, bank accounts and categories.
//
// Adding and updating are separate commands rather than one that means both, because an update names the thing
// it is changing and an add cannot.
//
// Setting the primary representative is its own command, because exactly one representative is primary at all
// times and that is a move rather than an edit.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Domain.Suppliers;

public sealed record AddRepresentativeCommand(string FullName, string Email, string? Phone, string? Position);

public sealed record UpdateRepresentativeCommand(Guid RepresentativeId, string FullName, string Email, string? Phone, string? Position);

public sealed record RemoveRepresentativeCommand(Guid RepresentativeId);

public sealed record SetPrimaryRepresentativeCommand(Guid RepresentativeId);

public sealed record AddAddressCommand(AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed record UpdateAddressCommand(Guid AddressId, AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);

public sealed record RemoveAddressCommand(Guid AddressId);

public sealed record AddContactCommand(string FullName, string Email, string? Phone, string? Role);

public sealed record UpdateContactCommand(Guid ContactId, string FullName, string Email, string? Phone, string? Role);

public sealed record RemoveContactCommand(Guid ContactId);

public sealed record AddBranchCommand(string NameAr, string NameEn, Guid? AddressId);

public sealed record UpdateBranchCommand(Guid BranchId, string NameAr, string NameEn, Guid? AddressId, bool IsActive);

public sealed record RemoveBranchCommand(Guid BranchId);

public sealed record AddBankAccountCommand(string AccountHolderName, string BankName, string? BranchName, string AccountNumber, string? SwiftBic, string CurrencyCode);

public sealed record UpdateBankAccountCommand(Guid BankAccountId, string AccountHolderName, string BankName, string? BranchName, string? AccountNumber, string? SwiftBic, string CurrencyCode);

public sealed record RemoveBankAccountCommand(Guid BankAccountId);

public sealed record SetDefaultBankAccountCommand(Guid BankAccountId);

public sealed record RevealBankAccountCommand(Guid BankAccountId);

public sealed record LinkCategoryCommand(string CategoryCode);

public sealed record UnlinkCategoryCommand(string CategoryCode);

public sealed record SetPrimaryCategoryCommand(string CategoryCode);

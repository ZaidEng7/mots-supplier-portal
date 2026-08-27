using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public abstract record ProfileMutationResult
{
    public sealed record Success(SupplierDto Supplier) : ProfileMutationResult;
    public sealed record NotFoundOrOutOfScope : ProfileMutationResult;
    public sealed record InvalidState(string Reason) : ProfileMutationResult;
}

// --- Representative ---

public sealed record AddRepresentativeCommand(string FullName, string Email, string? Phone, string? Position);
public sealed record UpdateRepresentativeCommand(Guid RepresentativeId, string FullName, string Email, string? Phone, string? Position);
public sealed record RemoveRepresentativeCommand(Guid RepresentativeId);
public sealed record SetPrimaryRepresentativeCommand(Guid RepresentativeId);

public interface IManageRepresentativeHandler
{
    Task<ProfileMutationResult> AddAsync(AddRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> SetPrimaryAsync(SetPrimaryRepresentativeCommand command, CancellationToken ct);
}

// --- Address ---

public sealed record AddAddressCommand(AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);
public sealed record UpdateAddressCommand(Guid AddressId, AddressKind Kind, string Line1, string? Line2, string City, string RegionCode, string Country, string? PostalCode, double? Latitude, double? Longitude);
public sealed record RemoveAddressCommand(Guid AddressId);

public interface IManageAddressHandler
{
    Task<ProfileMutationResult> AddAsync(AddAddressCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateAddressCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveAddressCommand command, CancellationToken ct);
}

// --- Contact ---

public sealed record AddContactCommand(string FullName, string Email, string? Phone, string? Role);
public sealed record UpdateContactCommand(Guid ContactId, string FullName, string Email, string? Phone, string? Role);
public sealed record RemoveContactCommand(Guid ContactId);

public interface IManageContactHandler
{
    Task<ProfileMutationResult> AddAsync(AddContactCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateContactCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveContactCommand command, CancellationToken ct);
}

// --- Branch ---

public sealed record AddBranchCommand(string NameAr, string NameEn, Guid? AddressId);
public sealed record UpdateBranchCommand(Guid BranchId, string NameAr, string NameEn, Guid? AddressId, bool IsActive);
public sealed record RemoveBranchCommand(Guid BranchId);

public interface IManageBranchHandler
{
    Task<ProfileMutationResult> AddAsync(AddBranchCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateBranchCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveBranchCommand command, CancellationToken ct);
}

// --- Bank account ---

public sealed record AddBankAccountCommand(string AccountHolderName, string BankName, string? BranchName, string AccountNumber, string? SwiftBic, string CurrencyCode);
/// <summary>AccountNumber is optional: pass null to leave the existing encrypted value untouched
/// (so correcting the holder name doesn't force re-entering the account number), or a new value to
/// re-encrypt/re-mask it - same as Add, never stored/logged in plaintext.</summary>
public sealed record UpdateBankAccountCommand(Guid BankAccountId, string AccountHolderName, string BankName, string? BranchName, string? AccountNumber, string? SwiftBic, string CurrencyCode);
public sealed record RemoveBankAccountCommand(Guid BankAccountId);
public sealed record SetDefaultBankAccountCommand(Guid BankAccountId);
public sealed record RevealBankAccountCommand(Guid BankAccountId);

public abstract record RevealBankAccountResult
{
    public sealed record Success(string AccountNumber) : RevealBankAccountResult;
    public sealed record NotFoundOrOutOfScope : RevealBankAccountResult;
}

public interface IManageBankAccountHandler
{
    Task<ProfileMutationResult> AddAsync(AddBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> SetDefaultAsync(SetDefaultBankAccountCommand command, CancellationToken ct);
    Task<RevealBankAccountResult> RevealAsync(RevealBankAccountCommand command, CancellationToken ct);
}

// --- Category link ---

public sealed record LinkCategoryCommand(string CategoryCode);
public sealed record UnlinkCategoryCommand(string CategoryCode);

public interface IManageCategoryLinkHandler
{
    Task<ProfileMutationResult> LinkAsync(LinkCategoryCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UnlinkAsync(UnlinkCategoryCommand command, CancellationToken ct);
}

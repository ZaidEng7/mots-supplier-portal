// What the writes to a supplier's attached things are called: one interface per kind, each carrying that
// kind's add, update and remove.
//
// They are grouped per kind rather than one interface per operation, because the three operations on an
// address are resolved together by the routes that manage addresses, and splitting them would be three
// registrations for one screen.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Domain.Suppliers;

public interface IManageRepresentativeHandler
{
    Task<ProfileMutationResult> AddAsync(AddRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveRepresentativeCommand command, CancellationToken ct);
    Task<ProfileMutationResult> SetPrimaryAsync(SetPrimaryRepresentativeCommand command, CancellationToken ct);
}

public interface IManageAddressHandler
{
    Task<ProfileMutationResult> AddAsync(AddAddressCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateAddressCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveAddressCommand command, CancellationToken ct);
}

public interface IManageContactHandler
{
    Task<ProfileMutationResult> AddAsync(AddContactCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateContactCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveContactCommand command, CancellationToken ct);
}

public interface IManageBranchHandler
{
    Task<ProfileMutationResult> AddAsync(AddBranchCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateBranchCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveBranchCommand command, CancellationToken ct);
}

public interface IManageBankAccountHandler
{
    Task<ProfileMutationResult> AddAsync(AddBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UpdateAsync(UpdateBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> RemoveAsync(RemoveBankAccountCommand command, CancellationToken ct);
    Task<ProfileMutationResult> SetDefaultAsync(SetDefaultBankAccountCommand command, CancellationToken ct);
    Task<RevealBankAccountResult> RevealAsync(RevealBankAccountCommand command, CancellationToken ct);
}

public interface IManageCategoryLinkHandler
{
    Task<ProfileMutationResult> LinkAsync(LinkCategoryCommand command, CancellationToken ct);
    Task<ProfileMutationResult> UnlinkAsync(UnlinkCategoryCommand command, CancellationToken ct);
    Task<ProfileMutationResult> SetPrimaryAsync(SetPrimaryCategoryCommand command, CancellationToken ct);
}

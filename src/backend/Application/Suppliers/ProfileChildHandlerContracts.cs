using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

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
}

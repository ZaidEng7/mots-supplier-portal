// The vocabulary for buying organizations: what one looks like, what can be asked of it, and what the
// answers are.
//
// An organization carries a department tree, and the shapes here mirror that: the organization itself,
// one department, and the manual link between a supplier and an organization.
//
// Two separate result types rather than one. An organization write can succeed, find nothing, or be
// refused by a rule. A supplier link can also be refused because the link already exists, which is not
// a rule violation and is worth saying plainly, so it has its own outcome.
//
// Removing a supplier link answers a plain yes or no rather than a result type, because the only two
// things that can happen are that it was removed or it was not there.

namespace MotsSupplierPortal.Application.Organizations;

using MotsSupplierPortal.Domain.Organizations;

public sealed record OrganizationDto(Guid Id, string LegalNameAr, string LegalNameEn, OrganizationType OrganizationType, string? ContactEmail, string? ContactPhone, bool IsActive, IReadOnlyList<OrgUnitDto> OrgUnits);
public sealed record OrgUnitDto(Guid Id, Guid OrganizationId, Guid? ParentOrgUnitId, string Name);
public sealed record SupplierOrgLinkDto(Guid Id, Guid SupplierId, string SupplierReferenceCode, Guid OrganizationId, DateTimeOffset CreatedAt);

public abstract record OrganizationMutationResult
{
    public sealed record Success(OrganizationDto Organization) : OrganizationMutationResult;
    public sealed record NotFound : OrganizationMutationResult;
    public sealed record InvalidState(string Reason) : OrganizationMutationResult;
}

public sealed record CreateOrganizationCommand(string LegalNameAr, string LegalNameEn, OrganizationType OrganizationType, string? ContactEmail, string? ContactPhone);

public interface ICreateOrganizationHandler
{
    Task<OrganizationMutationResult> HandleAsync(CreateOrganizationCommand command, CancellationToken ct);
}

public interface IListOrganizationsHandler
{
    Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken ct);
}

public sealed record AddOrgUnitCommand(Guid OrganizationId, string Name, Guid? ParentOrgUnitId);
public sealed record RemoveOrgUnitCommand(Guid OrganizationId, Guid OrgUnitId);

public interface IManageOrgUnitHandler
{
    Task<OrganizationMutationResult> AddAsync(AddOrgUnitCommand command, CancellationToken ct);
    Task<OrganizationMutationResult> RemoveAsync(RemoveOrgUnitCommand command, CancellationToken ct);
}

public abstract record SupplierOrgLinkMutationResult
{
    public sealed record Success(SupplierOrgLinkDto Link) : SupplierOrgLinkMutationResult;
    public sealed record NotFound : SupplierOrgLinkMutationResult;
    public sealed record AlreadyLinked : SupplierOrgLinkMutationResult;
}

public sealed record CreateSupplierOrgLinkCommand(string SupplierReferenceCode, Guid OrganizationId);
public sealed record RemoveSupplierOrgLinkCommand(Guid LinkId);

public interface IManageSupplierOrgLinkHandler
{
    Task<SupplierOrgLinkMutationResult> CreateAsync(CreateSupplierOrgLinkCommand command, CancellationToken ct);
    Task<bool> RemoveAsync(RemoveSupplierOrgLinkCommand command, CancellationToken ct);
    Task<IReadOnlyList<SupplierOrgLinkDto>> ListForSupplierAsync(string supplierReferenceCode, CancellationToken ct);
}

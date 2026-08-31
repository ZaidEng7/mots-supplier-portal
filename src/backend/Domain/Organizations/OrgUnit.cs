namespace MotsSupplierPortal.Domain.Organizations;

/// <summary>DOMAIN-MODEL.md §5.2: department/committee grouping within an Organization,
/// self-nesting via ParentOrgUnitId. Only ever constructed through Organization.AddOrgUnit,
/// which is what guarantees every unit belongs to exactly one Organization and the tree has no
/// cycles - this type has no public factory of its own.</summary>
public sealed class OrgUnit
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? ParentOrgUnitId { get; init; }
    public required string Name { get; set; }
}

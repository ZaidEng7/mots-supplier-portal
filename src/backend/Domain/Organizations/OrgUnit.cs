// A department or committee inside a buying body, which may itself sit under another one.
//
// There is no public way to build one. A unit is only ever created through
// Organization.AddOrgUnit, and that is what guarantees two things: every unit belongs to
// exactly one organization, and the tree can never contain a cycle, because a new unit can
// only name a parent that already exists.

namespace MotsSupplierPortal.Domain.Organizations;

public sealed class OrgUnit
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? ParentOrgUnitId { get; init; }
    public required string Name { get; set; }
}

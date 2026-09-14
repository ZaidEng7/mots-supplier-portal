// One supplier trades with one buying body. A supplier may have many of these, because a
// supplier may work with several bodies.
//
// The link is its own record rather than a collection on either side, so neither the
// supplier nor the organization owns the relationship.

namespace MotsSupplierPortal.Domain.Organizations;

using MotsSupplierPortal.Domain.Suppliers;

public sealed class SupplierOrgLink
{
    public Guid Id { get; private init; }
    public Guid SupplierId { get; private init; }
    public Guid OrganizationId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private SupplierOrgLink() { }

    public static SupplierOrgLink Create(Guid supplierId, Guid organizationId)
    {
        if (supplierId == Guid.Empty) throw new DomainException("SupplierOrgLink requires a real SupplierId.");
        if (organizationId == Guid.Empty) throw new DomainException("SupplierOrgLink requires a real OrganizationId.");

        return new SupplierOrgLink
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplierId,
            OrganizationId = organizationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

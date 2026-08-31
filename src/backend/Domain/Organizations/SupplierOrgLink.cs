using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Organizations;

/// <summary>BRULE-010/DOMAIN-MODEL.md §5.2: the many-to-many join between Supplier and
/// Organization ("a supplier may transact with many companies", Discovery §3.2.2). Deliberately
/// not owned by either aggregate's collection (Supplier.cs is untouched by Task #7/Stage A) -
/// linking behavior is Stage C, this stage only makes the row shape exist.</summary>
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

namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FEAT-06.1/FR-OFF-001: what a supplier says it can provide, for buyer discovery.
/// Deliberately its own root rather than a member of the Supplier aggregate's owned collections
/// (Representative[], Address[], etc.) - nothing about Offering participates in Supplier's own
/// invariants (submit-completeness, primary-representative, and so on), so there is no reason to
/// route every read/write through Supplier. SupplierId is a plain FK, same shape as CategoryLink.</summary>
public sealed class Offering
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public string? Description { get; set; }
    public required string CategoryCode { get; set; }
    public required string UnitOfMeasureCode { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>FEAT-06.2 [ASSUMPTION]: flexible key-value attributes (e.g. "capacity": "50
    /// guests"), not a per-category enforced schema - no per-category attribute-schema entity
    /// exists anywhere in reference data to bind against, and FEAT-06.2's own AC ("attribute
    /// schema by category") describes a real admin surface no one has asked for here. Same jsonb
    /// convention as AuditLog.Changes/OutboxMessage.PayloadJson: a plain string column holding
    /// serialized JSON, not EF's native JSON mapping.</summary>
    public string? AttributesJson { get; set; }
}

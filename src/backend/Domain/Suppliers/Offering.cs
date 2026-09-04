using MotsSupplierPortal.Domain.Common;
namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FEAT-06.1/FR-OFF-001: what a supplier says it can provide, for buyer discovery.
/// Deliberately its own root rather than a member of the Supplier aggregate's owned collections
/// (Representative[], Address[], etc.) - nothing about Offering participates in Supplier's own
/// invariants (submit-completeness, primary-representative, and so on), so there is no reason to
/// route every read/write through Supplier. SupplierId is a plain FK, same shape as CategoryLink.</summary>
public sealed class Offering : IVersionedAggregate
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

    /// <summary>
    /// T-029: §8.1's xmin-backed version. Offering is the aggregate where its absence bit hardest -
    /// a supplier's catalogue is edited by every supplier_user at that supplier, so two people
    /// editing one offering silently overwrote each other with no error and no trace.
    ///
    /// <para>No migration: xmin is a Postgres system column that already exists on every table, so
    /// this is a mapping change rather than a schema one.</para>
    /// </summary>
    public uint RowVersion { get; private set; }

    /// <summary>FEAT-06.2 [ASSUMPTION]: flexible key-value attributes (e.g. "capacity": "50
    /// guests"), not a per-category enforced schema - no per-category attribute-schema entity
    /// exists anywhere in reference data to bind against, and FEAT-06.2's own AC ("attribute
    /// schema by category") describes a real admin surface no one has asked for here. Same jsonb
    /// convention as AuditLog.Changes/OutboxMessage.PayloadJson: a plain string column holding
    /// serialized JSON, not EF's native JSON mapping.</summary>
    public string? AttributesJson { get; set; }
}

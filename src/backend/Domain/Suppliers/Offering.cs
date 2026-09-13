// Something a supplier says it can provide, so buyers can find it.
//
// It is its own record rather than part of the supplier, because nothing about an offering takes part
// in the supplier's own rules, such as profile completeness or having exactly one primary
// representative. There is no reason to route every read and write through the supplier, so SupplierId
// is a plain link, the same shape a category link uses.
//
// RowVersion is the version that refuses a lost update, and this is the record where its absence hurt
// most: a supplier's catalogue is edited by every user at that company, so two people editing one
// offering silently overwrote each other with no error and no trace.
//
// AttributesJson holds flexible extra attributes as JSON, such as a capacity of fifty guests. It is
// deliberately not a schema enforced per category: no per-category attribute schema exists anywhere in
// reference data to check against, and building the admin screen to define one is a feature nobody has
// asked for here. Serialised JSON in a plain string column is the same convention the audit log's
// changes and the outbox's payload use.

namespace MotsSupplierPortal.Domain.Suppliers;

using MotsSupplierPortal.Domain.Common;

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

    public uint RowVersion { get; private set; }

    public string? AttributesJson { get; set; }
}

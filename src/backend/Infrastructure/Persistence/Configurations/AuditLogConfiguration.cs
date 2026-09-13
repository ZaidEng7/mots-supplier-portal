// How an audit row maps to its table, and the four indexes that make it readable.
//
// One index matches the paging order of a supplier's own trail. Without an index matching that sort,
// cursor paging still returns the right rows and degrades at depth exactly like the row-skipping it
// replaced, which is the cost it exists to avoid.
//
// The three filters staff search by, the record, the actor and the date range, were already covered by
// the indexes above it. That was checked before assuming a gap, because an earlier audit had claimed the
// indexes existed and went unused.
//
// The action was the one dimension with no index. A search by action alone, or by action and date, would
// otherwise be a full scan of a table that grows forever by design.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> entity)
    {
        entity.ToTable("audit_log", "ops");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.ActorKind).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.AggregateType).HasMaxLength(100).IsRequired();
        entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
        entity.Property(a => a.Changes).HasColumnType("jsonb");
        entity.HasIndex(a => new { a.AggregateType, a.AggregateId, a.OccurredAt });
        entity.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
        entity.HasIndex(a => a.CorrelationId);
        entity.HasIndex(a => new { a.OccurredAt, a.Id });
        entity.HasIndex(a => new { a.Action, a.OccurredAt });
    }
}

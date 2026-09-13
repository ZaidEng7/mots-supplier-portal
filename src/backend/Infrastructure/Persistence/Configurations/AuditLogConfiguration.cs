using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

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
        // Supports the keyset scan on the own-trail read (MSP-66). Without an index matching the
        // (OccurredAt, Id) sort, keyset paging still returns correct rows but degrades at depth
        // exactly like the OFFSET it replaced - the cost it exists to avoid.
        entity.HasIndex(a => new { a.OccurredAt, a.Id });
        // MSP-75/FR-AUD-004: the entity, actor, and date-range filters above were already
        // covered by the three indexes above this one - checked before assuming a gap existed,
        // per the earlier audit finding's own claim that the indexes "exist, unused". Action was
        // the one dimension with no matching index; an action-only or action+date filter on this
        // table would otherwise be a full scan of a table that grows forever by design.
        entity.HasIndex(a => new { a.Action, a.OccurredAt });
    }
}

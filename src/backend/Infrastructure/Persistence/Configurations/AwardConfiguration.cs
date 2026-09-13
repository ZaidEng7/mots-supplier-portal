// How an award maps to its table.
//
// One award per tender, enforced by a unique index rather than only by the code that creates it.
//
// The comparison snapshot is stored as a structured document because it is written once and read whole; it
// is a record of what the figures were at the moment of the decision, not something later queries filter
// on.
//
// Deleting an award deletes its approval steps, because a step is part of the award rather than a record
// that outlives it.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Awards;

internal sealed class AwardConfiguration : IEntityTypeConfiguration<Award>
{
    public void Configure(EntityTypeBuilder<Award> entity)
    {
        entity.ToTable("award", "award");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.State).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.JustificationAr).HasMaxLength(4000).IsRequired();
        entity.Property(a => a.JustificationEn).HasMaxLength(4000).IsRequired();
        entity.Property(a => a.ComparisonSnapshotJson).HasColumnType("jsonb");
        entity.Property(a => a.ErpSyncStatus).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.ExternalPurchaseOrderRef).HasMaxLength(100);
        entity.Property(a => a.RowVersion).IsAppManagedVersion();
        entity.HasIndex(a => a.RfqId).IsUnique();
        entity.HasMany(a => a.Approvals).WithOne().HasForeignKey(p => p.AwardId).OnDelete(DeleteBehavior.Cascade);
    }
}

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

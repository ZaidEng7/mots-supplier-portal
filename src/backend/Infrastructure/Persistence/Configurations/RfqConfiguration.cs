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

internal sealed class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> entity)
    {
        entity.ToTable("rfq", "rfq");
        // EPIC-20; see the Supplier entity for why generated and why 'simple'.
        entity.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql(
                "to_tsvector('simple', regexp_replace(coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true);
        entity.HasIndex("SearchVector").HasMethod("GIN");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.ReferenceCode).HasMaxLength(30).IsRequired();
        entity.HasIndex(r => r.ReferenceCode).IsUnique();
        entity.Property(r => r.TitleAr).HasMaxLength(300).IsRequired();
        entity.Property(r => r.TitleEn).HasMaxLength(300).IsRequired();
        entity.Property(r => r.DescriptionAr).HasMaxLength(4000);
        entity.Property(r => r.DescriptionEn).HasMaxLength(4000);
        entity.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
        entity.Property(r => r.EvaluationTemplateSnapshotJson).HasColumnType("jsonb");
        entity.Property(r => r.CancelReason).HasMaxLength(2000);
        entity.Property(r => r.RowVersion).IsAppManagedVersion();
        entity.HasIndex(r => new { r.OrganizationId, r.State });
        entity.HasIndex(r => r.State);
        // A-7: "Awaiting my action" and the buyer list's mine/unassigned filter both query on
        // this beside the organization, which is already the first clause of every dashboard
        // query - so the composite, not a bare index on the owner.
        entity.HasIndex(r => new { r.OrganizationId, r.OwnerUserId });
        entity.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Requirements).WithOne().HasForeignKey(q => q.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Attachments).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Approvals).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Invitations).WithOne().HasForeignKey(i => i.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Clarifications).WithOne().HasForeignKey(c => c.RfqId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(r => r.Addenda).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
    }
}

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

internal sealed class RepresentativeConfiguration : IEntityTypeConfiguration<Representative>
{
    public void Configure(EntityTypeBuilder<Representative> entity)
    {
        entity.ToTable("representative", "supplier");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.FullName).HasMaxLength(200).IsRequired();
        entity.Property(r => r.Email).HasMaxLength(320).IsRequired();
        entity.HasIndex(r => r.SupplierId).HasFilter("\"IsPrimary\" = true").IsUnique();
    }
}

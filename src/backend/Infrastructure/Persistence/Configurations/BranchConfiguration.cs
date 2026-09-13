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

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> entity)
    {
        entity.ToTable("branch", "supplier");
        entity.HasKey(b => b.Id);
        entity.Property(b => b.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(b => b.NameEn).HasMaxLength(200).IsRequired();
        entity.HasIndex(b => b.SupplierId);
    }
}

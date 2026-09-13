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

internal sealed class CategoryLinkConfiguration : IEntityTypeConfiguration<CategoryLink>
{
    public void Configure(EntityTypeBuilder<CategoryLink> entity)
    {
        entity.ToTable("category_link", "supplier");
        entity.HasKey(l => l.Id);
        entity.Property(l => l.CategoryCode).HasMaxLength(50).IsRequired();
        entity.HasIndex(l => new { l.SupplierId, l.CategoryCode }).IsUnique();
    }
}

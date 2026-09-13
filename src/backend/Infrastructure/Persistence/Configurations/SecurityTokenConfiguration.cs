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

internal sealed class SecurityTokenConfiguration : IEntityTypeConfiguration<SecurityToken>
{
    public void Configure(EntityTypeBuilder<SecurityToken> entity)
    {
        entity.ToTable("security_token", "identity");
        entity.HasKey(t => t.Id);
        entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        entity.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30);
        entity.HasIndex(t => t.TokenHash).IsUnique();
        entity.HasIndex(t => t.UserId);
    }
}

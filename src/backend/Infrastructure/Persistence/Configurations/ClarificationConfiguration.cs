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

internal sealed class ClarificationConfiguration : IEntityTypeConfiguration<Clarification>
{
    public void Configure(EntityTypeBuilder<Clarification> entity)
    {
        entity.ToTable("clarification", "rfq");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Question).HasMaxLength(4000).IsRequired();
        entity.Property(c => c.Answer).HasMaxLength(4000);
        entity.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(c => c.RfqId);
        entity.HasIndex(c => c.AskedBySupplierId);
    }
}

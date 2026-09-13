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

internal sealed class ReferenceCodeCounterConfiguration : IEntityTypeConfiguration<ReferenceCodeCounter>
{
    public void Configure(EntityTypeBuilder<ReferenceCodeCounter> entity)
    {
        entity.ToTable("reference_code_counter", "supplier");
        // The prefix is the natural key; there is no surrogate id because a second row for the
        // same prefix would be a second, competing allocator.
        entity.HasKey(c => c.Prefix);
        entity.Property(c => c.Prefix).HasMaxLength(30);
        entity.Property(c => c.LastValue).IsRequired();
    }
}

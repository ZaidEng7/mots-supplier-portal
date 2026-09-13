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

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<MotsSupplierPortal.Domain.Evaluation.Evaluation>
{
    public void Configure(EntityTypeBuilder<MotsSupplierPortal.Domain.Evaluation.Evaluation> entity)
    {
        entity.ToTable("evaluation", "evaluation");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
        entity.Property(e => e.RowVersion).IsAppManagedVersion();
        entity.HasIndex(e => e.RfqId).IsUnique();
        entity.HasMany(e => e.Criteria).WithOne().HasForeignKey(c => c.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Assignments).WithOne().HasForeignKey(a => a.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Scores).WithOne().HasForeignKey(s => s.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Results).WithOne().HasForeignKey(r => r.EvaluationId).OnDelete(DeleteBehavior.Cascade);
    }
}

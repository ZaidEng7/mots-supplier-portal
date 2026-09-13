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

internal sealed class EvaluationAssignmentConfiguration : IEntityTypeConfiguration<EvaluationAssignment>
{
    public void Configure(EntityTypeBuilder<EvaluationAssignment> entity)
    {
        entity.ToTable("evaluation_assignment", "evaluation");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.RecusalReason).HasMaxLength(2000);
        entity.Ignore(a => a.IsActive);
        entity.HasIndex(a => new { a.EvaluationId, a.EvaluatorUserId }).IsUnique();
    }
}

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

internal sealed class ConsolidatedResultConfiguration : IEntityTypeConfiguration<ConsolidatedResult>
{
    public void Configure(EntityTypeBuilder<ConsolidatedResult> entity)
    {
        entity.ToTable("consolidated_result", "evaluation");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.TechnicalWeightedScore).HasPrecision(8, 2);
        entity.Property(r => r.FinancialWeightedScore).HasPrecision(8, 2);
        entity.Property(r => r.WeightedTotal).HasPrecision(8, 2);
        entity.HasIndex(r => new { r.EvaluationId, r.ProposalId }).IsUnique();
    }
}

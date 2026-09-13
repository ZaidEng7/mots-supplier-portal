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

internal sealed class EvaluatorScoreConfiguration : IEntityTypeConfiguration<EvaluatorScore>
{
    public void Configure(EntityTypeBuilder<EvaluatorScore> entity)
    {
        entity.ToTable("evaluator_score", "evaluation");
        entity.HasKey(s => s.Id);
        entity.Property(s => s.RawScore).HasPrecision(6, 2);
        entity.Property(s => s.CommentAr).HasMaxLength(2000);
        entity.Property(s => s.CommentEn).HasMaxLength(2000);
        entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId, s.ProposalId, s.CriterionId }).IsUnique();
        entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId });
    }
}

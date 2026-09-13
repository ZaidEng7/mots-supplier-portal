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

internal sealed class RequirementAnswerConfiguration : IEntityTypeConfiguration<RequirementAnswer>
{
    public void Configure(EntityTypeBuilder<RequirementAnswer> entity)
    {
        entity.ToTable("requirement_answer", "proposal");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.AnswerAr).HasMaxLength(4000).IsRequired();
        entity.Property(a => a.AnswerEn).HasMaxLength(4000).IsRequired();
        entity.HasIndex(a => new { a.ProposalId, a.RequirementId }).IsUnique();
    }
}

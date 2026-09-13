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

internal sealed class DocumentExpiryReminderConfiguration : IEntityTypeConfiguration<DocumentExpiryReminder>
{
    public void Configure(EntityTypeBuilder<DocumentExpiryReminder> entity)
    {
        entity.ToTable("document_expiry_reminder", "supplier");
        entity.HasKey(r => r.Id);

        // The unique index IS the de-duplication rule. Checking in C# and inserting afterwards
        // leaves a window between the read and the write, and this job can legitimately run
        // concurrently with itself (a retry overlapping a scheduled run). A duplicate insert
        // must fail at the database, not merely be unlikely.
        entity.HasIndex(r => new { r.SupplierDocumentId, r.DocumentVersion, r.ThresholdDays })
            .IsUnique();

        entity.HasOne<SupplierDocument>().WithMany()
            .HasForeignKey(r => r.SupplierDocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

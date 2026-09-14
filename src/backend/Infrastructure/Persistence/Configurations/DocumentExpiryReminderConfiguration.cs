// How a sent expiry reminder maps to its table.
//
// The unique index is the de-duplication rule, not a safety net over one written in code.
//
// Checking in code and inserting afterwards leaves a window between the read and the write, and this job
// can legitimately run at the same time as itself, because a retry can overlap a scheduled run. A
// duplicate insert must fail at the database rather than merely be unlikely.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class DocumentExpiryReminderConfiguration : IEntityTypeConfiguration<DocumentExpiryReminder>
{
    public void Configure(EntityTypeBuilder<DocumentExpiryReminder> entity)
    {
        entity.ToTable("document_expiry_reminder", "supplier");
        entity.HasKey(r => r.Id);

        entity.HasIndex(r => new { r.SupplierDocumentId, r.DocumentVersion, r.ThresholdDays })
            .IsUnique();

        entity.HasOne<SupplierDocument>().WithMany()
            .HasForeignKey(r => r.SupplierDocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

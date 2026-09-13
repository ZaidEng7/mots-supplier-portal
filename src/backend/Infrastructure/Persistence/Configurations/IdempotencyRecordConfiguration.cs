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

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<Domain.Idempotency.IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<Domain.Idempotency.IdempotencyRecord> entity)
    {
        entity.ToTable("idempotency_record", "ops");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.Key).HasMaxLength(200).IsRequired();
        entity.Property(r => r.RequestFingerprint).HasMaxLength(64).IsRequired();
        // TEXT, not jsonb. §8.2.3 requires the stored response to be "replayed verbatim", and
        // jsonb normalises: it reorders keys and re-spaces the document, so a replay came back
        // byte-different from the original even though the data was identical. A client comparing
        // responses, or hashing one, would see two different answers to the same request.
        entity.Property(r => r.ResponseBody).HasColumnType("text");

        // The UNIQUE constraint is the reservation. Two concurrent retries of the same submission
        // both try to insert, and Postgres lets exactly one through - the loser gets a duplicate-key
        // violation, which is how the second click is refused without a lock or a read-then-write
        // race. Scoped by UserId so a client-generated key cannot collide across callers.
        entity.HasIndex(r => new { r.UserId, r.Key }).IsUnique();

        // The GC job scans by expiry.
        entity.HasIndex(r => r.ExpiresAt);
    }
}

// How a remembered response maps to its table.
//
//
// THE STORED RESPONSE IS TEXT AND NOT STRUCTURED JSON
//
// The written contract requires the stored response to be replayed exactly as it was sent, and the
// structured type normalises: it reorders keys and re-spaces the document.
//
// So a replay came back byte-different from the original even though the data was identical. A client
// comparing two responses, or hashing one, would see two different answers to the same request.
//
//
// THE UNIQUE CONSTRAINT IS THE RESERVATION
//
// Two concurrent retries of the same submission both try to insert, and the database lets exactly one
// through. The loser gets a duplicate-key violation, which is how the second click is refused without a
// lock and without a read-then-write race.
//
// It is scoped by the user, so a key a client generates cannot collide with another caller's.
//
// The last index exists because the cleanup job scans by expiry.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<Domain.Idempotency.IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<Domain.Idempotency.IdempotencyRecord> entity)
    {
        entity.ToTable("idempotency_record", "ops");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.Key).HasMaxLength(200).IsRequired();
        entity.Property(r => r.RequestFingerprint).HasMaxLength(64).IsRequired();
        entity.Property(r => r.ResponseBody).HasColumnType("text");

        entity.HasIndex(r => new { r.UserId, r.Key }).IsUnique();

        entity.HasIndex(r => r.ExpiresAt);
    }
}

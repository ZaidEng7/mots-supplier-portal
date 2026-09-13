// How a queued outbound message maps to its table.
//
// The payload is a structured document because nothing queries inside it; it is written by whoever
// enqueues and read whole by whoever sends.
//
// The index is on the status, because that is the only question the dispatcher asks: what has not been
// sent yet.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Common;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("outbox_message", "ops");
        entity.HasKey(o => o.Id);
        entity.Property(o => o.Type).HasMaxLength(200).IsRequired();
        entity.Property(o => o.PayloadJson).HasColumnType("jsonb").IsRequired();
        entity.Property(o => o.SyncStatus).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(o => o.SyncStatus);
    }
}

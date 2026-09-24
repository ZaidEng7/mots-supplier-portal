// How an API key maps to its table, and why the prefix is the only index it needs.
//
// THE PREFIX IS UNIQUE AND INDEXED because it is read on every authenticated request a feed receives. A lookup
// by prefix is one row; without the index it is a scan of the table, on the hot path of the one caller we built
// this for.
//
// UNIQUENESS IS ENFORCED BY THE DATABASE rather than checked before insert. Two keys sharing a prefix would
// make verification ambiguous, and a check-then-insert in application code is exactly the shape that two
// simultaneous creations walk through.
//
// IT LIVES IN ops, beside the audit log, because it belongs to running the system rather than to any of the
// business areas that own the other schemas. A schema of its own for one table would be a new place to look for
// no gain.
//
// PERMISSIONS AND ALLOWEDIPRANGES ARE POSTGRES ARRAYS, the same treatment the review annotation gives its
// flagged fields. A joined string would have to be split by every reader and would sort and index as prose.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotsSupplierPortal.Domain.Integration;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> entity)
    {
        entity.ToTable("api_key", "ops");
        entity.HasKey(k => k.Id);
        entity.Property(k => k.Name).HasMaxLength(200).IsRequired();
        entity.Property(k => k.Prefix).HasMaxLength(ApiKey.PrefixLength).IsRequired();
        entity.Property(k => k.SecretHash).HasMaxLength(200).IsRequired();
        entity.Property(k => k.Salt).HasMaxLength(100).IsRequired();
        entity.Property(k => k.Permissions).HasColumnType("text[]").IsRequired();
        entity.Property(k => k.AllowedIpRanges).HasColumnType("text[]").IsRequired();
        entity.HasIndex(k => k.Prefix).IsUnique();
    }
}

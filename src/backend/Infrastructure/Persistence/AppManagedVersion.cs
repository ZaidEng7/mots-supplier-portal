using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MotsSupplierPortal.Infrastructure.Persistence;

/// <summary>
/// T-030/D-15: the mapping for an application-managed row version, replacing
/// <c>IsRowVersion()</c>'s Postgres <c>xmin</c> on all nine versioned roots.
/// </summary>
public static class AppManagedVersion
{
    /// <summary>
    /// Maps a <c>uint RowVersion</c> as a concurrency token the APPLICATION advances.
    ///
    /// <para><b>Not <c>IsRowVersion()</c>.</b> That marks the property store-generated, which is
    /// correct for <c>xmin</c> and fatal here: EF would refuse to send a value it believes the
    /// database produces, so the bump in <c>SaveChangesAsync</c> would never reach a column.
    /// <c>IsConcurrencyToken()</c> keeps the <c>WHERE RowVersion = @original</c> predicate - which is
    /// the entire guard - while leaving the value ours to set.</para>
    ///
    /// <para><b>Stored as <c>bigint</c>, not as an unsigned type.</b> Postgres has no unsigned
    /// integer, and Npgsql's nearest mappings for <c>uint</c> are the system types <c>oid</c> and
    /// <c>xid</c> - reusing one of those to hold an application counter would be a column whose type
    /// says "this is a Postgres internal". A converter to <c>long</c> keeps the domain property a
    /// <c>uint</c>, so §8.1's wire format and every ETag site are untouched, and gives the column an
    /// ordinary type a DBA can read. <c>uint</c> cannot overflow <c>bigint</c>, so the conversion is
    /// lossless in both directions.</para>
    ///
    /// <para>Default 1 rather than 0, so a row's first version is a value a client could plausibly
    /// have read. Nothing depends on it, but a zero version reads like "unset".</para>
    /// </summary>
    public static PropertyBuilder<uint> IsAppManagedVersion(this PropertyBuilder<uint> builder) =>
        builder
            .IsConcurrencyToken()
            .HasConversion(new ValueConverter<uint, long>(v => v, v => (uint)v))
            .HasColumnType("bigint")
            .HasColumnName("RowVersion")
            .HasDefaultValue(1u);
}

// The mapping for a row version the APPLICATION advances, on all thirteen versioned roots.
//
// It replaces the database's own row identifier, which the written decision records as the wrong token for an
// application-managed version.
//
//
// NOT THE STORE-GENERATED FORM
//
// That marks the property as produced by the database, which is correct for the database's own row identifier and
// fatal here: the mapper would refuse to send a value it believes the database produces, so the bump in the save
// would never reach a column.
//
// Marking it a concurrency token keeps the condition on the update, which is the entire guard, while leaving the
// value ours to set.
//
//
// STORED AS A SIGNED SIXTY-FOUR-BIT INTEGER
//
// The database has no unsigned integer type, and the provider's nearest mappings for the domain's type are two of
// the database's own internal types. Reusing one of those to hold an application counter would be a column whose
// type says "this is a database internal".
//
// A conversion keeps the domain property unsigned, so the wire format and every version header are untouched, and
// gives the column an ordinary type an administrator can read. The conversion is lossless in both directions.
//
// It defaults to one rather than zero, so a row's first version is a value a client could plausibly have read.
// Nothing depends on it, but a zero version reads like unset.

namespace MotsSupplierPortal.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public static class AppManagedVersion
{
    public static PropertyBuilder<uint> IsAppManagedVersion(this PropertyBuilder<uint> builder) =>
        builder
            .IsConcurrencyToken()
            .HasConversion(new ValueConverter<uint, long>(v => v, v => (uint)v))
            .HasColumnType("bigint")
            .HasColumnName("RowVersion")
            .HasDefaultValue(1u);
}

// How a catalogue entry maps to its table, and its search vector.
//
// The vector serves the cross-entity search, and it is deliberately not a replacement for the pair of
// substring matches the catalogue's own search endpoint uses.
//
// That endpoint's callers get substring matching today, so "ater" finds "Catering". A prefix query does
// not, so swapping it would narrow shipped behaviour without anybody asking. The catalogue keeps its own
// semantics until somebody decides they should change.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class OfferingConfiguration : IEntityTypeConfiguration<Offering>
{
    public void Configure(EntityTypeBuilder<Offering> entity)
    {
        entity.ToTable("offering", "supplier");
        entity.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql(
                "to_tsvector('simple', regexp_replace(coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true);
        entity.HasIndex("SearchVector").HasMethod("GIN");
        entity.Property(o => o.RowVersion).IsAppManagedVersion();
        entity.HasKey(o => o.Id);
        entity.Property(o => o.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(o => o.NameEn).HasMaxLength(200).IsRequired();
        entity.Property(o => o.Description).HasMaxLength(2000);
        entity.Property(o => o.CategoryCode).HasMaxLength(50).IsRequired();
        entity.Property(o => o.UnitOfMeasureCode).HasMaxLength(50).IsRequired();
        entity.Property(o => o.PriceAmount).HasPrecision(18, 2);
        entity.Property(o => o.CurrencyCode).HasMaxLength(10);
        entity.Property(o => o.AttributesJson).HasColumnType("jsonb");
        entity.HasIndex(o => o.SupplierId);
    }
}

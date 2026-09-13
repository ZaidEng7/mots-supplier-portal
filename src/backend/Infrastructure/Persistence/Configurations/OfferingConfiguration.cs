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

internal sealed class OfferingConfiguration : IEntityTypeConfiguration<Offering>
{
    public void Configure(EntityTypeBuilder<Offering> entity)
    {
        entity.ToTable("offering", "supplier");
        // EPIC-20. NOT a replacement for the ILIKE pair in SearchBuyerOfferingsHandler, deliberately:
        // that endpoint's callers get substring matching today ("ater" finds "Catering") and a tsquery
        // prefix does not, so swapping it would narrow a shipped behaviour without anyone asking. This
        // vector serves the cross-entity search; the catalogue keeps its own semantics until someone
        // decides they should change.
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

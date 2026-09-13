// How a delivery term maps to its table, and the eleven the standard defines.
//
// The code column is three characters, like the currency table and unlike the other reference tables. The
// standard's codes are three letters, and a column that accepts fifty invites free text back in through
// the administration screen.
//
// All eleven terms are seeded, in the standard's own order: the seven for any mode of transport, then the
// four for sea and inland waterway.
//
// The English name is the standards body's own. The Arabic is the term as Syrian tender documents write
// it, with the code kept inside the name, because that is how a bidder reads it on paper.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class IncotermConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.Incoterm>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.Incoterm> entity)
    {
        entity.ToTable("incoterm", "reference");
        entity.HasKey(i => i.Id);
        entity.Property(i => i.Code).HasMaxLength(3).IsRequired();
        entity.HasIndex(i => i.Code).IsUnique();
        entity.Property(i => i.NameAr).HasMaxLength(150).IsRequired();
        entity.Property(i => i.NameEn).HasMaxLength(150).IsRequired();

        entity.HasData(
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000601"), Code = "EXW", NameAr = "تسليم المصنع", NameEn = "Ex Works" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000602"), Code = "FCA", NameAr = "تسليم الناقل", NameEn = "Free Carrier" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000603"), Code = "CPT", NameAr = "النقل مدفوع حتى", NameEn = "Carriage Paid To" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000604"), Code = "CIP", NameAr = "النقل والتأمين مدفوعان حتى", NameEn = "Carriage and Insurance Paid To" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000605"), Code = "DAP", NameAr = "التسليم في المكان", NameEn = "Delivered at Place" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000606"), Code = "DPU", NameAr = "التسليم في المكان بعد التفريغ", NameEn = "Delivered at Place Unloaded" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000607"), Code = "DDP", NameAr = "التسليم خالص الرسوم", NameEn = "Delivered Duty Paid" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000608"), Code = "FAS", NameAr = "التسليم بجانب السفينة", NameEn = "Free Alongside Ship" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-000000000609"), Code = "FOB", NameAr = "التسليم على ظهر السفينة", NameEn = "Free on Board" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-00000000060A"), Code = "CFR", NameAr = "التكلفة وأجرة الشحن", NameEn = "Cost and Freight" },
            new Domain.ReferenceData.Incoterm { Id = Guid.Parse("00000000-0000-0000-0000-00000000060B"), Code = "CIF", NameAr = "التكلفة والتأمين وأجرة الشحن", NameEn = "Cost, Insurance and Freight" }
        );
    }
}

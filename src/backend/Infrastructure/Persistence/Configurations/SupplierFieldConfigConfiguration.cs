// How an administrator's switch over one supplier field maps to its table, and what the switches start as.
//
// The seeded rows reproduce exactly the behaviour that used to be written into the code, so switching this
// table on changed nothing on the day it shipped. Every row is editable through the administration
// endpoints afterwards.
//
// One row is different and is an assumption rather than a reproduction. The ministry's commercial-visibility
// flag is seeded off, because the written rule names aggregate figures only as the default and tags the
// question itself as needing business confirmation. The ministry's legal office's answer flips that row.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class SupplierFieldConfigConfiguration : IEntityTypeConfiguration<Domain.Configuration.SupplierFieldConfig>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.SupplierFieldConfig> entity)
    {
        entity.ToTable("supplier_field_config", "ops");
        entity.Property(c => c.RowVersion).IsAppManagedVersion();
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Category).HasMaxLength(50).IsRequired();
        entity.Property(c => c.FieldCode).HasMaxLength(50).IsRequired();
        entity.HasIndex(c => new { c.Category, c.FieldCode }).IsUnique();

        entity.HasData(
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000401"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "legalInfo", IsEnabled = true },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000402"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "bankAccount", IsEnabled = true },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000403"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "categoryLink", IsEnabled = true },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000411"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "legalNameAr", IsEnabled = true },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000412"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "legalNameEn", IsEnabled = true },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000413"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "registrationNumber", IsEnabled = false },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000414"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "taxId", IsEnabled = false },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000415"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "supplierType", IsEnabled = false },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000416"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "establishedOn", IsEnabled = false },
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000421"), Category = Domain.Configuration.FieldConfigCategory.GovernanceVisibility, FieldCode = "commercialValues", IsEnabled = false }
        );
    }
}

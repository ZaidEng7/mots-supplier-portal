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

        // FEAT-04.9/FEAT-04.2 [ASSUMPTION 2026-08-27]: seeded to reproduce exactly the
        // previously-hardcoded behavior - editable via admin endpoints thereafter.
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
            // D-6/BRULE-087: the Ministry's commercial-visibility flag, seeded OFF. BRULE-087
            // names aggregate-only as the default and tags the question itself as
            // [REQUIRES BUSINESS CONFIRMATION], so MOT Legal's answer flips this row.
            new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000421"), Category = Domain.Configuration.FieldConfigCategory.GovernanceVisibility, FieldCode = "commercialValues", IsEnabled = false }
        );
    }
}

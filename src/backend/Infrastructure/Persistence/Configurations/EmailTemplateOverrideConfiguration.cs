// How an administrator's email rewording maps to its table.
//
// The body columns are generous because these are formatted email bodies, and the shipped ones are
// already several hundred characters before an administrator adds a paragraph of their own.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class EmailTemplateOverrideConfiguration : IEntityTypeConfiguration<Domain.Configuration.EmailTemplateOverride>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.EmailTemplateOverride> entity)
    {
        entity.ToTable("email_template_override", "ops");
        entity.Property(o => o.RowVersion).IsAppManagedVersion();
        entity.HasKey(o => o.Id);
        entity.Property(o => o.Key).HasMaxLength(100).IsRequired();
        entity.Property(o => o.SubjectAr).HasMaxLength(300).IsRequired();
        entity.Property(o => o.SubjectEn).HasMaxLength(300).IsRequired();
        entity.Property(o => o.BodyAr).HasMaxLength(4000).IsRequired();
        entity.Property(o => o.BodyEn).HasMaxLength(4000).IsRequired();
        entity.HasIndex(o => o.Key).IsUnique();
    }
}

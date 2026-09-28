// Where an integration's address and credential are stored.
//
// The key is unique because code looks connections up by it, and two rows claiming to be "erp" would resolve to
// whichever the database returned first.
//
// The seeded row carries no address, which is what makes the deployment's own settings stay in force until
// somebody saves one on the screen. It exists so the screen has something to show on a fresh database rather than
// an empty list that reads as "this product has no integrations".

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotsSupplierPortal.Domain.Integration;

internal sealed class IntegrationConnectionConfiguration : IEntityTypeConfiguration<IntegrationConnection>
{
    public void Configure(EntityTypeBuilder<IntegrationConnection> entity)
    {
        entity.ToTable("integration_connection", "ops");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Key).HasMaxLength(50).IsRequired();
        entity.HasIndex(c => c.Key).IsUnique();
        entity.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        entity.Property(c => c.BaseUrl).HasMaxLength(500).IsRequired();
        entity.Property(c => c.ApiKey).HasMaxLength(200).IsRequired();
        entity.Property(c => c.SecretCipher).HasMaxLength(2000);
        entity.Property(c => c.LastTestDetail).HasMaxLength(1000);

        entity.HasData(new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000901"),
            Key = IntegrationConnection.ErpKey,
            DisplayName = "Seven Gates ERP",
            BaseUrl = string.Empty,
            ApiKey = string.Empty,
            IsEnabled = false,
        });
    }
}

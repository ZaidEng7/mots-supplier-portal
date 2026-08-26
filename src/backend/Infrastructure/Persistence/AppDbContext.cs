using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.ReferenceData;

namespace MotsSupplierPortal.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Currency>(entity =>
        {
            entity.ToTable("currencies");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).HasMaxLength(3).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();
            entity.Property(c => c.NameAr).HasMaxLength(100).IsRequired();
            entity.Property(c => c.NameEn).HasMaxLength(100).IsRequired();

            entity.HasData(
                new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Code = "SYP", NameAr = "ليرة سورية", NameEn = "Syrian Pound" },
                new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar" }
            );
        });
    }
}

// How a single-use token, for confirming an email address or resetting a password, maps to its table.
//
// Only the hash is stored, and it is unique, so presenting a token is a single indexed lookup and a
// stolen database yields nothing that can be presented.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;

internal sealed class SecurityTokenConfiguration : IEntityTypeConfiguration<SecurityToken>
{
    public void Configure(EntityTypeBuilder<SecurityToken> entity)
    {
        entity.ToTable("security_token", "identity");
        entity.HasKey(t => t.Id);
        entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        entity.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30);
        entity.HasIndex(t => t.TokenHash).IsUnique();
        entity.HasIndex(t => t.UserId);
    }
}

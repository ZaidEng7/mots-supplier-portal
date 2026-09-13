// How one sign-in session maps to its table.
//
// Only the hash of the token is stored, never the token, so a stolen database cannot be used to sign in
// as anybody.
//
// The family index is what makes reuse detection cheap: rotating a token keeps the family, so one query
// on it finds every descendant of a session that has to be revoked together.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.ToTable("user_session", "identity");
        entity.HasKey(t => t.Id);
        entity.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        entity.HasIndex(t => t.UserId);
        entity.HasIndex(t => t.FamilyId);
    }
}

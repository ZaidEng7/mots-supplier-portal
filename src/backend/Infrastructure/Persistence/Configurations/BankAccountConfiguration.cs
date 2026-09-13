// How a supplier's bank account maps to its table.
//
// Two columns hold the account number: the encrypted one, which has no length limit because the
// ciphertext is longer than the value and grows with the scheme, and the masked one, which is what every
// screen shows.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> entity)
    {
        entity.ToTable("bank_account", "supplier");
        entity.HasKey(b => b.Id);
        entity.Property(b => b.AccountHolderName).HasMaxLength(200).IsRequired();
        entity.Property(b => b.BankName).HasMaxLength(200).IsRequired();
        entity.Property(b => b.BranchName).HasMaxLength(200);
        entity.Property(b => b.EncryptedAccountNumber).IsRequired();
        entity.Property(b => b.MaskedAccountNumber).HasMaxLength(50).IsRequired();
        entity.Property(b => b.SwiftBic).HasMaxLength(20);
        entity.Property(b => b.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.HasIndex(b => b.SupplierId);
    }
}

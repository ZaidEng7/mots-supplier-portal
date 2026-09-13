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

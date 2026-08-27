using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Security;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.6/FR-PROF-006. The account number is never stored or logged in plaintext -
/// see FieldEncryptionService and BankAccount for the encrypt/mask split. Reveal is a distinct,
/// separately-permissioned, audited action (BRULE-014/090/091).</summary>
public sealed class ManageBankAccountHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, FieldEncryptionService encryption) : IManageBankAccountHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var encrypted = encryption.Encrypt(command.AccountNumber);
        var masked = FieldEncryptionService.Mask(command.AccountNumber);

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "bankAccount", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        Domain.Suppliers.BankAccount account;
        try
        {
            account = supplier.AddBankAccount(command.AccountHolderName, command.BankName, command.BranchName, encrypted, masked, command.SwiftBic, command.CurrencyCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // BankAccount.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking
        // heuristic would otherwise mark it Modified (no-op UPDATE) instead of Added - track it
        // explicitly.
        db.BankAccounts.Add(account);

        // Never log/audit the raw account number - only that a bank account was added, and the
        // masked value (BRULE-091: no PII in logs).
        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_added", Guid.NewGuid(), scope.UserId, reason: masked, referenceCode: supplier.ReferenceCode, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        // AccountNumber is optional on edit - only re-encrypt/re-mask when the caller is actually
        // changing it, so correcting the holder name doesn't force re-entering the account number.
        byte[]? encrypted = null;
        string? masked = null;
        if (!string.IsNullOrEmpty(command.AccountNumber))
        {
            encrypted = encryption.Encrypt(command.AccountNumber);
            masked = FieldEncryptionService.Mask(command.AccountNumber);
        }

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "bankAccount", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        try
        {
            supplier.UpdateBankAccount(command.BankAccountId, command.AccountHolderName, command.BankName, command.BranchName, encrypted, masked, command.SwiftBic, command.CurrencyCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // Never log/audit the raw account number - only that it changed, and the masked value if
        // the account number itself was part of the edit (BRULE-091: no PII in logs).
        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_updated", Guid.NewGuid(), scope.UserId, reason: masked, referenceCode: supplier.ReferenceCode, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "bankAccount", defaultValue: true, ct);

        var stateBefore = supplier.OnboardingState;
        try
        {
            supplier.RemoveBankAccount(command.BankAccountId, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_removed", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, stateBefore, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> SetDefaultAsync(SetDefaultBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        try
        {
            supplier.SetDefaultBankAccount(command.BankAccountId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_set_default", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<RevealBankAccountResult> RevealAsync(RevealBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new RevealBankAccountResult.NotFoundOrOutOfScope();
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == command.BankAccountId && b.SupplierId == scope.SupplierId, ct);
        if (account is null) return new RevealBankAccountResult.NotFoundOrOutOfScope();

        var plaintext = encryption.Decrypt(account.EncryptedAccountNumber);

        // The reveal itself is the sensitive event to audit (BRULE-014/090/091) - never the
        // plaintext value itself, only that it was accessed and by whom.
        await auditLogger.LogAsync("Supplier", account.SupplierId, "bank_account_revealed", Guid.NewGuid(), scope.UserId, reason: account.MaskedAccountNumber, ct: ct);

        return new RevealBankAccountResult.Success(plaintext);
    }
}

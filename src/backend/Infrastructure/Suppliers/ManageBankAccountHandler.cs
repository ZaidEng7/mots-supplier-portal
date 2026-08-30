using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Security;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.6/FR-PROF-006. The account number is never stored or logged in plaintext -
/// see FieldEncryptionService and BankAccount for the encrypt/mask split. Reveal is a distinct,
/// separately-permissioned, audited action (BRULE-014/090/091). The `changes` audit diff only
/// ever carries the masked account number, never the encrypted bytes or plaintext.</summary>
public sealed class ManageBankAccountHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, FieldEncryptionService encryption) : IManageBankAccountHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.BankAccount, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var encrypted = encryption.Encrypt(command.AccountNumber);
        var masked = FieldEncryptionService.Mask(command.AccountNumber);

        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "bankAccount", defaultValue: true, ct);

        Domain.Suppliers.BankAccount account;
        bool reTriggered;
        try
        {
            (account, reTriggered) = supplier.AddBankAccount(command.AccountHolderName, command.BankName, command.BranchName, encrypted, masked, command.SwiftBic, command.CurrencyCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // BankAccount.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking
        // heuristic would otherwise mark it Modified (no-op UPDATE) instead of Added - track it
        // explicitly.
        db.BankAccounts.Add(account);

        var changes = AuditChangeBuilder.Build(
            ("accountHolderName", null, command.AccountHolderName),
            ("bankName", null, command.BankName),
            ("branchName", null, command.BranchName),
            ("maskedAccountNumber", null, masked),
            ("currencyCode", null, command.CurrencyCode));

        // Never log/audit the raw account number - only that a bank account was added, and the
        // masked value (BRULE-091: no PII in logs).
        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_added", scope.UserId, reason: masked, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, reTriggered, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.BankAccount, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.BankAccounts.FirstOrDefault(b => b.Id == command.BankAccountId);

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

        bool reTriggered;
        try
        {
            reTriggered = supplier.UpdateBankAccount(command.BankAccountId, command.AccountHolderName, command.BankName, command.BranchName, encrypted, masked, command.SwiftBic, command.CurrencyCode, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("accountHolderName", before?.AccountHolderName, command.AccountHolderName),
            ("bankName", before?.BankName, command.BankName),
            ("branchName", before?.BranchName, command.BranchName),
            ("maskedAccountNumber", before?.MaskedAccountNumber, masked ?? before?.MaskedAccountNumber),
            ("currencyCode", before?.CurrencyCode, command.CurrencyCode));

        // Never log/audit the raw account number - only that it changed, and the masked value if
        // the account number itself was part of the edit (BRULE-091: no PII in logs).
        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_updated", scope.UserId, reason: masked, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, reTriggered, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.BankAccount, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.BankAccounts.FirstOrDefault(b => b.Id == command.BankAccountId);
        var isComplianceCritical = await SupplierFieldConfigLookup.IsEnabledAsync(db, FieldConfigCategory.ComplianceRetrigger, "bankAccount", defaultValue: true, ct);

        bool reTriggered;
        try
        {
            reTriggered = supplier.RemoveBankAccount(command.BankAccountId, isComplianceCritical);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("bankName", before?.BankName, null),
            ("maskedAccountNumber", before?.MaskedAccountNumber, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_removed", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await ComplianceReTrigger.LogIfReTriggeredAsync(db, auditLogger, supplier, reTriggered, "bankAccount", scope.UserId, ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> SetDefaultAsync(SetDefaultBankAccountCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.BankAccount, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var wasDefault = supplier.BankAccounts.FirstOrDefault(b => b.Id == command.BankAccountId)?.IsDefault;
        try
        {
            supplier.SetDefaultBankAccount(command.BankAccountId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(("isDefault", wasDefault, true));

        await auditLogger.LogAsync("Supplier", supplier.Id, "bank_account_set_default", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
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
        await auditLogger.LogAsync("Supplier", account.SupplierId, "bank_account_revealed", scope.UserId, reason: account.MaskedAccountNumber, ct: ct);

        return new RevealBankAccountResult.Success(plaintext);
    }
}

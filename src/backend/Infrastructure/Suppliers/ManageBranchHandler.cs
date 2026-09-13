// Adding, editing and removing a supplier's branches.
//
// The new row is added to the tracked set explicitly. Its identifier is assigned by us rather than by the
// database, so the graph-tracking heuristic would otherwise take it for an existing row and issue a
// pointless update instead of an insert.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ManageBranchHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageBranchHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Branch, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        Domain.Suppliers.Branch branch;
        try
        {
            branch = supplier.AddBranch(command.NameAr, command.NameEn, command.AddressId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        db.Branches.Add(branch);

        var changes = AuditChangeBuilder.Build(
            ("nameAr", null, command.NameAr),
            ("nameEn", null, command.NameEn),
            ("addressId", null, command.AddressId?.ToString()));

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_added", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Branch, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.Branches.FirstOrDefault(b => b.Id == command.BranchId);
        try
        {
            supplier.UpdateBranch(command.BranchId, command.NameAr, command.NameEn, command.AddressId, command.IsActive);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("nameAr", before?.NameAr, command.NameAr),
            ("nameEn", before?.NameEn, command.NameEn),
            ("addressId", before?.AddressId?.ToString(), command.AddressId?.ToString()),
            ("isActive", before?.IsActive, command.IsActive));

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_updated", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Branch, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.Branches.FirstOrDefault(b => b.Id == command.BranchId);
        try
        {
            supplier.RemoveBranch(command.BranchId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("nameAr", before?.NameAr, null),
            ("nameEn", before?.NameEn, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_removed", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

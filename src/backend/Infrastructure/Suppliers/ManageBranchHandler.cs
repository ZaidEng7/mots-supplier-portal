using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.5/FR-PROF-005.</summary>
public sealed class ManageBranchHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageBranchHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        Domain.Suppliers.Branch branch;
        try
        {
            branch = supplier.AddBranch(command.NameAr, command.NameEn, command.AddressId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // Branch.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking heuristic
        // would otherwise mark it Modified (no-op UPDATE) instead of Added - track it explicitly.
        db.Branches.Add(branch);

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_added", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        try
        {
            supplier.UpdateBranch(command.BranchId, command.NameAr, command.NameEn, command.AddressId, command.IsActive);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_updated", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveBranchCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        try
        {
            supplier.RemoveBranch(command.BranchId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "branch_removed", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

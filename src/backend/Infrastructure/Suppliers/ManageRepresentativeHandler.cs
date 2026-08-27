using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.4/FR-PROF-004: add/edit/remove representatives with primary designation,
/// enforcing DOMAIN-MODEL.md's "exactly one primary at all times" invariant for real (not just at
/// registration).</summary>
public sealed class ManageRepresentativeHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRepresentativeHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        Representative representative;
        try
        {
            representative = supplier.AddRepresentative(command.FullName, command.Email, command.Phone, command.Position);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // Representative.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking
        // heuristic would otherwise mark it Modified (no-op UPDATE) instead of Added - track it
        // explicitly.
        db.Representatives.Add(representative);

        var changes = AuditChangeBuilder.Build(
            ("fullName", null, command.FullName),
            ("email", null, command.Email),
            ("phone", null, command.Phone),
            ("position", null, command.Position));

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_added", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var before = supplier.Representatives.FirstOrDefault(r => r.Id == command.RepresentativeId);
        try
        {
            supplier.UpdateRepresentative(command.RepresentativeId, command.FullName, command.Email, command.Phone, command.Position);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("fullName", before?.FullName, command.FullName),
            ("email", before?.Email, command.Email),
            ("phone", before?.Phone, command.Phone),
            ("position", before?.Position, command.Position));

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_updated", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var before = supplier.Representatives.FirstOrDefault(r => r.Id == command.RepresentativeId);
        try
        {
            supplier.RemoveRepresentative(command.RepresentativeId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("fullName", before?.FullName, null),
            ("email", before?.Email, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_removed", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> SetPrimaryAsync(SetPrimaryRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var previousPrimary = supplier.Representatives.FirstOrDefault(r => r.IsPrimary);
        try
        {
            supplier.SetPrimaryRepresentative(command.RepresentativeId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("previousPrimaryRepresentativeId", previousPrimary?.Id.ToString(), null),
            ("newPrimaryRepresentativeId", null, command.RepresentativeId.ToString()));

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_set_primary", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

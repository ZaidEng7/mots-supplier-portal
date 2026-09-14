// Adding, editing, removing and re-designating a supplier's authorised representatives.
//
// The invariant the written model states, exactly one primary at all times, is enforced here for real
// rather than only at registration.
//
// A new representative is added to the tracked set explicitly. Its identifier is assigned by us rather than
// by the database, so the graph-tracking heuristic would otherwise take it for an existing row and issue a
// pointless update instead of an insert.
//
//
// WHY SWAPPING THE PRIMARY SAVES TWICE
//
// The table carries a unique index covering primary representatives only, so at most one primary per
// supplier is enforced by the database.
//
// It is an index rather than a constraint, so it cannot be deferred to the end of the transaction: deferral
// needs a table constraint, and constraints do not support the condition a partial index needs.
//
// Clearing the old primary and setting the new one in a single save therefore risks the mapper issuing the
// promotion before the demotion, which the index checks statement by statement and rejects as a momentary
// duplicate. Reproduced: promoting a previously-demoted representative back failed with a duplicate-key
// error.
//
// Saving the demotion first, and committing it, means only one row can be primary at any moment regardless
// of the order the mapper chooses.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ManageRepresentativeHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRepresentativeHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Representative, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        Representative representative;
        try
        {
            representative = supplier.AddRepresentative(command.FullName, command.Email, command.Phone, command.Position);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        db.Representatives.Add(representative);

        var changes = AuditChangeBuilder.Build(
            ("fullName", null, command.FullName),
            ("email", null, command.Email),
            ("phone", null, command.Phone),
            ("position", null, command.Position));

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_added", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Representative, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

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

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_updated", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Representative, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

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

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_removed", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> SetPrimaryAsync(SetPrimaryRepresentativeCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Representative, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var previousPrimary = supplier.Representatives.FirstOrDefault(r => r.IsPrimary);

        if (previousPrimary is not null && previousPrimary.Id != command.RepresentativeId)
        {
            previousPrimary.IsPrimary = false;
            await db.SaveChangesAsync(ct);
        }

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

        await auditLogger.LogAsync("Supplier", supplier.Id, "representative_set_primary", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

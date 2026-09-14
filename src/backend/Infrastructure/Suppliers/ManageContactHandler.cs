// Adding, editing and removing a supplier's contact people.
//
// The new row is added to the tracked set explicitly. Its identifier is assigned by us rather than by the
// database, so the graph-tracking heuristic would otherwise take it for an existing row and issue a
// pointless update instead of an insert.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ManageContactHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageContactHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddContactCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Contact, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        Domain.Suppliers.Contact contact;
        try
        {
            contact = supplier.AddContact(command.FullName, command.Email, command.Phone, command.Role);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        db.Contacts.Add(contact);

        await auditLogger.LogAsync("Supplier", supplier.Id, "contact_added", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateContactCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Contact, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        try
        {
            supplier.UpdateContact(command.ContactId, command.FullName, command.Email, command.Phone, command.Role);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "contact_updated", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveContactCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Contact, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        try
        {
            supplier.RemoveContact(command.ContactId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "contact_removed", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

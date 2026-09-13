// Adding, editing and removing a supplier's addresses.
//
// A new address is added to the tracked set explicitly. Its identifier is assigned in the domain factory
// rather than by the database, and the graph-tracking heuristic infers an insert from an unset key, so
// without this it would take the row for an existing one and emit a pointless update.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ManageAddressHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageAddressHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Address, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        Domain.Suppliers.Address address;
        try
        {
            address = supplier.AddAddress(command.Kind, command.Line1, command.Line2, command.City, command.RegionCode, command.Country, command.PostalCode, command.Latitude, command.Longitude);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        db.Addresses.Add(address);

        var changes = AuditChangeBuilder.Build(
            ("kind", null, command.Kind.ToString()),
            ("line1", null, command.Line1),
            ("city", null, command.City),
            ("regionCode", null, command.RegionCode),
            ("country", null, command.Country));

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_added", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Address, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.Addresses.FirstOrDefault(a => a.Id == command.AddressId);
        try
        {
            supplier.UpdateAddress(command.AddressId, command.Kind, command.Line1, command.Line2, command.City, command.RegionCode, command.Country, command.PostalCode, command.Latitude, command.Longitude);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("kind", before?.Kind.ToString(), command.Kind.ToString()),
            ("line1", before?.Line1, command.Line1),
            ("city", before?.City, command.City),
            ("regionCode", before?.RegionCode, command.RegionCode),
            ("country", before?.Country, command.Country));

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_updated", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, ProfileFieldCodes.Address, ct);
        if (refusal is not null) return new ProfileMutationResult.NotEditable(refusal);

        var before = supplier.Addresses.FirstOrDefault(a => a.Id == command.AddressId);
        try
        {
            supplier.RemoveAddress(command.AddressId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        var changes = AuditChangeBuilder.Build(
            ("kind", before?.Kind.ToString(), null),
            ("line1", before?.Line1, null),
            ("city", before?.City, null));

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_removed", scope.UserId, referenceCode: supplier.ReferenceCode, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

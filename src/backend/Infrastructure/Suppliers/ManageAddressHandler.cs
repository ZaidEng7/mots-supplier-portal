using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.3/FR-PROF-003.</summary>
public sealed class ManageAddressHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageAddressHandler
{
    public async Task<ProfileMutationResult> AddAsync(AddAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        Domain.Suppliers.Address address;
        try
        {
            address = supplier.AddAddress(command.Kind, command.Line1, command.Line2, command.City, command.RegionCode, command.Country, command.PostalCode, command.Latitude, command.Longitude);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        // Address.Id is client-assigned (Guid.CreateVersion7()) in the domain factory, so EF's
        // graph-tracking heuristic (which infers Added vs Modified from whether the key already
        // has a non-default value) would otherwise mark it Modified and emit a no-op UPDATE
        // instead of an INSERT - track it explicitly.
        db.Addresses.Add(address);

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_added", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> UpdateAsync(UpdateAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        try
        {
            supplier.UpdateAddress(command.AddressId, command.Kind, command.Line1, command.Line2, command.City, command.RegionCode, command.Country, command.PostalCode, command.Latitude, command.Longitude);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_updated", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<ProfileMutationResult> RemoveAsync(RemoveAddressCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new ProfileMutationResult.NotFoundOrOutOfScope();
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);
        if (supplier is null) return new ProfileMutationResult.NotFoundOrOutOfScope();

        try
        {
            supplier.RemoveAddress(command.AddressId);
        }
        catch (DomainException ex)
        {
            return new ProfileMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Supplier", supplier.Id, "address_removed", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProfileMutationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

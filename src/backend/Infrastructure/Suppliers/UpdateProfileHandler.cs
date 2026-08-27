using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>STORY-03.1.1/STORY-04.1.1: edits are row-scoped and only legal while EmailVerified/
/// ProfileInProgress/InfoRequested - the domain itself refuses edits once Submitted (read-only).</summary>
public sealed class UpdateProfileHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IConcurrencyContext concurrency) : IUpdateProfileHandler
{
    public async Task<UpdateProfileResult> HandleAsync(UpdateProfileCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new UpdateProfileResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .IncludeProfile()
            .FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);

        if (supplier is null)
        {
            return new UpdateProfileResult.NotFoundOrOutOfScope();
        }

        SupplierConcurrency.ApplyExpectedVersion(db, supplier, concurrency);

        try
        {
            supplier.UpdateCoreProfile(command.Description, command.Website, command.SupplierGroup, command.CurrencyCode);
        }
        catch (DomainException ex)
        {
            return new UpdateProfileResult.InvalidState(ex.Message);
        }

        var primary = supplier.Representatives.FirstOrDefault(r => r.IsPrimary);
        if (primary is not null)
        {
            primary.Phone = command.PrimaryContactPhone;
        }

        // The audit write and the supplier UPDATE commit together (AuditLogger owns the
        // SaveChanges), so both live inside the guard.
        var persisted = await SupplierConcurrency.TryPersistAsync(async () =>
        {
            await auditLogger.LogAsync("Supplier", supplier.Id, "profile_updated", Guid.NewGuid(), scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
        });

        if (!persisted)
        {
            return new UpdateProfileResult.Conflict(await SupplierConcurrency.CurrentVersionAsync(db, supplier.Id, ct));
        }

        return new UpdateProfileResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}

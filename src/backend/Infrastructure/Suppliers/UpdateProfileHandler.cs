// Editing the core fields of a supplier's own profile.
//
// Edits are scoped to the caller's own row and are only legal in the three states before submission. The
// domain itself refuses them once the application is submitted, because a submitted application is
// read-only.
//
// An omitted field resolves to the value the record already holds rather than to nothing, which is what
// makes this a partial edit rather than a replacement.
//
//
// THE FLAGGED-FIELD CHECK KEYS OFF WHAT CHANGED, NOT WHAT WAS SENT
//
// Re-sending a value identical to the stored one is not an edit, and forms routinely round-trip every field
// they rendered; this one posts all five.
//
// Comparing values rather than presence keeps the guard correct regardless of how chatty the caller is,
// instead of depending on callers to send minimal payloads.
//
//
// THE CONCURRENCY GUARD WRAPS THE AUDIT WRITE TOO
//
// The audit row and the supplier update commit together, because the logger owns the save, so both have to
// sit inside the guard for a collision to be seen as one.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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

        var currentPhone = supplier.Representatives.FirstOrDefault(r => r.IsPrimary)?.Phone;
        var touched = new List<string>();
        if (Changed(command.Description, supplier.Description)) touched.Add(ProfileFieldCodes.Description);
        if (Changed(command.Website, supplier.Website)) touched.Add(ProfileFieldCodes.Website);
        if (Changed(command.SupplierGroup, supplier.SupplierGroup)) touched.Add(ProfileFieldCodes.SupplierGroup);
        if (Changed(command.CurrencyCode, supplier.CurrencyCode)) touched.Add(ProfileFieldCodes.CurrencyCode);
        if (Changed(command.PrimaryContactPhone, currentPhone)) touched.Add(ProfileFieldCodes.PrimaryContactPhone);

        var refusal = await FlaggedFieldGuard.RefusalReasonAsync(db, supplier, touched, ct);
        if (refusal is not null)
        {
            return new UpdateProfileResult.NotEditable(refusal);
        }

        SupplierConcurrency.ApplyExpectedVersion(db, supplier, concurrency);

        try
        {
            supplier.UpdateCoreProfile(
                command.Description.Or(supplier.Description),
                command.Website.Or(supplier.Website),
                command.SupplierGroup.Or(supplier.SupplierGroup),
                command.CurrencyCode.Or(supplier.CurrencyCode));
        }
        catch (DomainException ex)
        {
            return new UpdateProfileResult.InvalidState(ex.Message);
        }

        var primary = supplier.Representatives.FirstOrDefault(r => r.IsPrimary);
        if (primary is not null && command.PrimaryContactPhone.IsSet)
        {
            primary.Phone = command.PrimaryContactPhone.Value;
        }

        var persisted = await SupplierConcurrency.TryPersistAsync(async () =>
        {
            await auditLogger.LogAsync("Supplier", supplier.Id, "profile_updated", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
        });

        if (!persisted)
        {
            return new UpdateProfileResult.Conflict(await SupplierConcurrency.CurrentVersionAsync(db, supplier.Id, ct));
        }

        return new UpdateProfileResult.Success(SupplierDtoMapper.ToDto(supplier));
    }

    private static bool Changed(Patch<string?> patch, string? current) =>
        patch.IsSet && !string.Equals(patch.Value, current, StringComparison.Ordinal);
}

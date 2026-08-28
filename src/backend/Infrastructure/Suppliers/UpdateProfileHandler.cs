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

        // MSP-77: the flagged-field check keys off fields whose value actually CHANGES, not merely
        // fields that appear in the payload. Re-sending a value identical to the stored one is not
        // an edit, and forms routinely round-trip every field they rendered - the SPA's profile
        // form posts all five. Comparing values rather than presence keeps the guard correct
        // regardless of how chatty the client is, instead of depending on clients to send minimal
        // payloads.
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
            // Or(current) is what makes this a PATCH: an omitted field resolves to the value the
            // entity already holds instead of null.
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

    private static bool Changed(Patch<string?> patch, string? current) =>
        patch.IsSet && !string.Equals(patch.Value, current, StringComparison.Ordinal);
}

// Removing one rewording, which restores the shipped string.
//
// A hard delete, and here that is right: the row IS the override, so removing it is the whole operation.
// Deactivating instead would leave a row that means nothing.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DeleteUiStringOverrideHandler(AppDbContext db) : IDeleteUiStringOverrideHandler
{
    public async Task<bool> HandleAsync(string key, string language, CancellationToken ct)
    {
        var existing = await db.UiStringOverrides
            .FirstOrDefaultAsync(o => o.Key == key && o.Language == language, ct);
        if (existing is null) return false;

        db.UiStringOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

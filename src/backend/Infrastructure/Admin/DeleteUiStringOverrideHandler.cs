using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

public sealed class DeleteUiStringOverrideHandler(AppDbContext db) : IDeleteUiStringOverrideHandler
{
    public async Task<bool> HandleAsync(string key, string language, CancellationToken ct)
    {
        var existing = await db.UiStringOverrides
            .FirstOrDefaultAsync(o => o.Key == key && o.Language == language, ct);
        if (existing is null) return false;

        // A hard delete, and here it is right: the row IS the override, so removing it restores the
        // shipped string. Deactivating instead would leave a row that means nothing.
        db.UiStringOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

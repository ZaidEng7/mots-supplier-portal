// What makes the supplier record's "somebody else changed this" check real.
//
// Before this, the version was mapped and returned in the response, but nothing ever sent it back and no
// handler set an expected value. So the mapper compared the version it had just read against itself, always
// matched, and the second of two concurrent writers silently overwrote the first. The token was decoration.
//
// Setting the caller's expected version as the original value is what puts it into the update's condition,
// so the database rather than the application decides whether the row moved underneath us.
//
//
// A WRITE WITH NO EXPECTED VERSION IS ALLOWED THROUGH
//
// Deliberate, and worth stating. Rejecting every version-less write would have broken every existing caller
// on the day it shipped.
//
// So the guard is opt-in per caller today, and the interface opts in on the screens where two people
// realistically edit the same fields. Making it mandatory is a separate decision, once every caller sends
// the header.
//
//
// WHY THE PERSIST STEP IS PASSED IN RATHER THAN CALLED HERE
//
// The audit logger saves on its own, so the guarded update is actually committed inside the audit call. A
// catch placed after that call never sees the collision.
//
// That internal save is a wider wrinkle affecting every audit call site and is deliberately not changed
// here; a separate piece of work centralises the logger.
//
// The winner's current version is re-read on a fresh untracked query, because the failed context still
// holds the stale value and reading through the tracker would hand the client back the version it already
// had.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class SupplierConcurrency
{
    public static void ApplyExpectedVersion(AppDbContext db, Supplier supplier, IConcurrencyContext concurrency)
    {
        if (concurrency.ExpectedRowVersion is not { } expected) return;

        db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = expected;
    }

    public static async Task<bool> TryPersistAsync(Func<Task> persist)
    {
        try
        {
            await persist();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public static async Task<uint> CurrentVersionAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == supplierId)
            .Select(s => s.RowVersion)
            .FirstOrDefaultAsync(ct);
}

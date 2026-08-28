using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// MSP-65: makes the Supplier row's optimistic concurrency real.
///
/// Before this, RowVersion was mapped (`xmin`) and returned in the DTO, but nothing ever sent it
/// back and no handler set an original value - so EF compared the version it had just read against
/// itself, always matched, and the second of two concurrent writers silently overwrote the first.
/// The token was decoration.
///
/// Policy on a missing If-Match: the write proceeds. This is deliberate and worth stating, because
/// the alternative (reject every version-less write) would break every existing client on the day
/// it shipped. It means the guard is opt-in per caller today; the SPA opts in for the screens where
/// two people realistically edit the same fields. Tightening to mandatory is a separate decision
/// once all callers send the header.
/// </summary>
internal static class SupplierConcurrency
{
    /// <summary>Tells EF to include the caller's expected version in the UPDATE's WHERE clause, so
    /// the database - not the application - decides whether the row moved underneath us.</summary>
    public static void ApplyExpectedVersion(AppDbContext db, Supplier supplier, IConcurrencyContext concurrency)
    {
        if (concurrency.ExpectedRowVersion is not { } expected) return;

        db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = expected;
    }

    /// <summary>
    /// Runs the handler's persist step and converts a concurrency collision into a typed result
    /// rather than an unhandled 500.
    ///
    /// Takes the work as a delegate rather than just calling SaveChanges, because
    /// <see cref="Audit.AuditLogger"/> performs its own SaveChanges internally - so the guarded
    /// UPDATE is actually committed inside the audit call, and a catch placed after it never sees
    /// the exception. (That internal SaveChanges is a wider design wrinkle affecting all 55 audit
    /// call sites; deliberately not changed here - see MSP-64, which will centralize the logger.)
    /// </summary>
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

    /// <summary>The winner's current version, read on a fresh no-tracking query - the failed
    /// context still holds the stale value, so re-reading through the tracker would hand the
    /// client back the version it already had.</summary>
    public static async Task<uint> CurrentVersionAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == supplierId)
            .Select(s => s.RowVersion)
            .FirstOrDefaultAsync(ct);
}

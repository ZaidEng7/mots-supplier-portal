using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Idempotency;

/// <summary>
/// T-053/§8.2.1: the retention half - <i>"persists … for 24 hours [ASSUMPTION] in a dedicated store
/// (Postgres table, GC'd by Hangfire)"</i>.
///
/// <para>Deletes by the row's own stored <c>ExpiresAt</c> rather than by re-deriving "24 hours ago".
/// If the retention window is ever changed, rows written under the old policy keep the expiry they
/// were promised instead of being retroactively expired or kept.</para>
///
/// <para>Deleting an expired record is not a data loss worth guarding: after the window a retry is a
/// new request by the contract's own definition, and §8.2 sets the window precisely so the store does
/// not grow without bound.</para>
/// </summary>
public sealed class IdempotencyCleanupJob(AppDbContext db, ILogger<IdempotencyCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var removed = await db.IdempotencyRecords
            .Where(r => r.ExpiresAt < DateTimeOffset.UtcNow)
            .ExecuteDeleteAsync(ct);

        // Logged rather than silent: a store that stops shrinking is the first sign the job has
        // stopped running, and a count of zero every day looks the same as a job that never fired.
        logger.LogInformation("Idempotency cleanup removed {Removed} expired record(s).", removed);
    }
}

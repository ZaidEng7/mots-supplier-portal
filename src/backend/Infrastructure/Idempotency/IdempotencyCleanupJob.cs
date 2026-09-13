// Deletes expired idempotency records, hourly.
//
// It deletes by the expiry each row was written with rather than by re-deriving a cut-off from today. If
// the retention window is ever changed, rows written under the old policy keep the expiry they were
// promised instead of being retroactively expired or kept.
//
// Deleting an expired record loses nothing worth guarding. After the window, a retry is a new request by
// the contract's own definition, and the window exists precisely so the table does not grow without
// bound.

namespace MotsSupplierPortal.Infrastructure.Idempotency;

using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class IdempotencyCleanupJob(AppDbContext db, ILogger<IdempotencyCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var removed = await db.IdempotencyRecords
            .Where(r => r.ExpiresAt < DateTimeOffset.UtcNow)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("Idempotency cleanup removed {Removed} expired record(s).", removed);
    }
}

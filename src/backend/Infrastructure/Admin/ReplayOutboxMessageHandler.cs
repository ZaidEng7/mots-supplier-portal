// Putting a failed outbox message back in the queue.
//
// Only a FAILED message is replayable. A pending one is already going to be attempted, and re-queuing a sent one
// would send an integration event twice. The outbox exists to make delivery exactly once, and an administrator's
// button that breaks that is worse than no button.
//
// That guard is the reason this is a handler rather than a bare update statement.
//
// Replaying resets the row and lets the dispatcher pick it up on its next pass. It does NOT dispatch inline,
// because that would put an external call on a request thread and give the operator a timeout instead of an
// answer.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class ReplayOutboxMessageHandler(AppDbContext db) : IReplayOutboxMessageHandler
{
    public async Task<bool> HandleAsync(Guid id, CancellationToken ct)
    {
        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || message.SyncStatus != OutboxSyncStatus.Failed) return false;

        message.SyncStatus = OutboxSyncStatus.Pending;
        message.ProcessedAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

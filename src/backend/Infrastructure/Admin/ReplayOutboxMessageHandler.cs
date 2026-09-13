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

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// SCR-722's action: put a message back in the queue.
///
/// <para><b>Only a Failed message is replayable.</b> A Pending one is already going to be attempted, and
/// re-queuing a Sent one would send an integration event twice - the outbox exists to make delivery
/// exactly-once, and an admin button that breaks that is worse than no button. The guard is the reason
/// this handler exists rather than a bare ExecuteUpdate.</para>
///
/// <para>Replay resets the row to Pending and clears ProcessedAt; the dispatcher picks it up on its next
/// pass. It does NOT dispatch inline: doing so would put an external call on an HTTP request thread and
/// give the operator a timeout instead of an answer.</para>
/// </summary>
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

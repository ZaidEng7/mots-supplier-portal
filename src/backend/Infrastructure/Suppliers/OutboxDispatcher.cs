// The job that actually delivers what the outbox has been collecting.
//
// The outbox table had two writers and no readers anywhere in the codebase, confirmed by searching rather
// than assumed from the ticket. Rows accumulated forever with nothing ever moving them out of pending.
//
// It runs on the recurring schedule, with the same durability model as the other recurring jobs: an
// interrupted run is retried, and anything already marked sent or failed is skipped next time, because only
// pending rows are selected.
//
// The batch is bounded per run rather than unbounded, on the same reasoning as every paged list here. A
// batch that grows with the backlog turns one slow run into a longer one instead of many bounded ones, and
// the schedule already provides the next chance at whatever this run did not reach.
//
//
// TWO KINDS OF MESSAGE, ONE ROAD
//
// Notification messages are turned into rows here rather than handed to the integration transport. They are
// the same kind of thing, work that must survive the commit of the state change that caused it, so they
// travel the same road; their destination is simply a notification row rather than an outbound integration.
//
//
// FAILURE IS TERMINAL FOR NOW, AND NOT RETRIED
//
// The only transport that exists today logs and cannot fail, so this path is currently unreachable in
// practice. A retry-with-backoff policy would be speculative complexity for a failure nothing can produce.
//
// Stated as a limitation rather than built ahead of the real transport that would need it. Revisit when
// there is one and failure becomes an observable outcome.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class OutboxDispatcher(
    AppDbContext db,
    IOutboxTransport transport,
    INotificationMaterialiser materialiser,
    ILogger<OutboxDispatcher> logger)
{
    public const int BatchSize = 100;

    public async Task DispatchPendingAsync(CancellationToken ct = default)
    {
        var pending = await db.OutboxMessages
            .Where(m => m.SyncStatus == OutboxSyncStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            try
            {
                if (message.Type == NotificationRequest.OutboxType)
                {
                    await materialiser.MaterialiseAsync(NotificationRequest.FromPayloadJson(message.PayloadJson), ct);
                }
                else
                {
                    await transport.SendAsync(message.Id, message.Type, message.PayloadJson, ct);
                }

                message.SyncStatus = OutboxSyncStatus.Sent;
            }
            catch (Exception ex)
            {
                message.SyncStatus = OutboxSyncStatus.Failed;
                logger.LogError(ex,
                    "Outbox message {OutboxMessageId} (type {OutboxMessageType}) failed to dispatch",
                    message.Id, message.Type);
            }

            message.ProcessedAt = DateTimeOffset.UtcNow;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}

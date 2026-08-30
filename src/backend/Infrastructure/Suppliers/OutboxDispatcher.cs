using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Task #16: the missing dispatcher-shaped hole. Before this, OutboxMessage had exactly 2 write
/// sites (ReviewApplicationHandlers.SendApplicationApprovedEmailAsync's caller, ComplianceReTrigger)
/// and 0 read sites anywhere in the codebase (confirmed by grep, not assumed from the ticket
/// description) - rows accumulated in ops.outbox_log (or wherever the table lives) forever, with
/// nothing ever setting SyncStatus away from Pending.
///
/// <para>Run as a Hangfire recurring job (Program.cs), same durability model as
/// DocumentExpiryJob/DraftCleanupJob - if a run is interrupted mid-batch, Hangfire retries the job,
/// and any message already marked Sent/Failed is simply skipped on the next pass (the WHERE clause
/// only selects Pending rows).</para>
///
/// <para><b>Failed is terminal for now, not retried.</b> With LoggingOutboxTransport as the only
/// transport that exists today, SendAsync never actually fails, so this path is presently
/// unreachable in practice - a real retry-with-backoff policy is speculative complexity for a
/// failure mode nothing can currently produce. Left as a stated limitation rather than built ahead
/// of the real EPIC-23 transport that would need it, per this session's own YAGNI standard - revisit
/// when EPIC-23 lands and Failed becomes a real, observable outcome.</para>
/// </summary>
public sealed class OutboxDispatcher(AppDbContext db, IOutboxTransport transport, ILogger<OutboxDispatcher> logger)
{
    /// <summary>Bounded per run, not unbounded - the same reasoning as every keyset-paged list in
    /// this codebase (MSP-66): a batch that grows with the backlog turns one slow run into a
    /// longer one instead of many bounded ones, and Hangfire's recurring schedule already provides
    /// the next chance to pick up whatever this run did not reach.</summary>
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
                await transport.SendAsync(message.Id, message.Type, message.PayloadJson, ct);
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

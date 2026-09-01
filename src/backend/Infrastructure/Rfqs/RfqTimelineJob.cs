using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-07.6/FR-PWF-004/FR-RFQ-007: Published -&gt; SubmissionOpen when
/// now &gt;= submissionOpensAt, and SubmissionOpen -&gt; SubmissionClosed when
/// now &gt;= submissionClosesAt (BUSINESS-PROCESSES.md §3.1 - both are `system` actor
/// transitions, no permission check). Same durable-recurring-job model as DocumentExpiryJob/
/// OutboxDispatcher (Program.cs registers this on a 5-minute cadence, matching OutboxDispatcher's
/// own cadence reasoning: RFQ deadlines are time-of-day precise, not daily, so a daily cadence
/// would make "the window opened at 9am" mean "up to a day late" for no reason).
///
/// <para><b>Idempotent by construction, not by a marker.</b> Each pass only selects RFQs still in
/// the source state (Published / SubmissionOpen) whose threshold has passed; once transitioned,
/// they no longer match the query, so a retry or an overlapping run cannot double-fire. No reason
/// is recorded for these transitions - they are scheduled, not early-close, and
/// CloseSubmissionWindow's own domain guard only requires a reason when isEarlyClose is true.</para></summary>
public sealed class RfqTimelineJob(AppDbContext db, IAuditLogger auditLogger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var toOpen = await db.Rfqs
            .Where(r => r.State == RfqState.Published && r.SubmissionOpensAt != null && r.SubmissionOpensAt <= now)
            .ToListAsync(ct);

        foreach (var rfq in toOpen)
        {
            rfq.OpenSubmissionWindow();
            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_opened", actorLabel: "system",
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Published), toState: nameof(RfqState.SubmissionOpen), ct: ct);
        }

        var toClose = await db.Rfqs
            .Where(r => r.State == RfqState.SubmissionOpen && r.SubmissionClosesAt != null && r.SubmissionClosesAt <= now)
            .ToListAsync(ct);

        foreach (var rfq in toClose)
        {
            rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);
            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_closed", actorLabel: "system",
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.SubmissionOpen), toState: nameof(RfqState.SubmissionClosed), ct: ct);
        }

        if (toOpen.Count > 0 || toClose.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}

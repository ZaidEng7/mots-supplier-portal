using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Proposals;
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

            // §3.1 "Published -> SubmissionOpen | In-app to invitees". A CLOCK-triggered transition,
            // but still a state change - so the notification travels the Outbox in this same
            // transaction (D-5), not a separate Hangfire enqueue. The clock decided WHEN; what is
            // being announced is the state change, and it must not outlive a rollback.
            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqSubmissionOpened,
                await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
                $"{NotificationTypes.RfqSubmissionOpened}:{rfq.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_opened", actorLabel: "system",
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Published), toState: nameof(RfqState.SubmissionOpen), ct: ct);
        }

        var toClose = await db.Rfqs
            .Where(r => r.State == RfqState.SubmissionOpen && r.SubmissionClosesAt != null && r.SubmissionClosesAt <= now)
            .ToListAsync(ct);

        // A-9/BRULE-052: the drafts the window is about to close on. Loaded BEFORE the transition so
        // the set is the one the closing applies to, and keyed by RFQ so each supplier is told about
        // their own bid rather than about the tender.
        var closingRfqIds = toClose.Select(r => r.Id).ToList();
        var lapsingDrafts = closingRfqIds.Count == 0
            ? []
            : await db.Proposals
                .Where(p => closingRfqIds.Contains(p.RfqId) && p.State == ProposalState.Draft)
                .ToListAsync(ct);

        foreach (var rfq in toClose)
        {
            rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);

            // §3.1 "SubmissionOpen -> SubmissionClosed | In-app to invitees + committee" - two
            // groups, both named by the table.
            var closedRecipients = await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct);
            closedRecipients.AddRange(await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct));
            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqSubmissionClosed, closedRecipients,
                $"{NotificationTypes.RfqSubmissionClosed}:{rfq.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_closed", actorLabel: "system",
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.SubmissionOpen), toState: nameof(RfqState.SubmissionClosed), ct: ct);

            // BRULE-052, enforced for the first time. A Draft that survived the window was previously
            // left in Draft forever: the supplier's dashboard kept counting a bid that could never be
            // submitted, and nothing in the record said what had happened to it.
            foreach (var draft in lapsingDrafts.Where(p => p.RfqId == rfq.Id))
            {
                draft.Lapse();

                NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalLapsed,
                    await NotificationRecipients.SupplierUsersAsync(db, draft.SupplierId, ct),
                    $"{NotificationTypes.ProposalLapsed}:{draft.Id}",
                    new Dictionary<string, string?>
                    {
                        ["rfqCode"] = rfq.ReferenceCode,
                        ["proposalCode"] = draft.ReferenceCode,
                        ["proposalId"] = draft.Id.ToString(),
                    });

                await auditLogger.LogAsync("Proposal", draft.Id, "proposal_lapsed", actorLabel: "system",
                    referenceCode: draft.ReferenceCode, fromState: nameof(ProposalState.Draft),
                    toState: nameof(ProposalState.Lapsed), ct: ct);
            }
        }

        if (toOpen.Count > 0 || toClose.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}

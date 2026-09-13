// The job that opens and closes submission windows when their moment arrives.
//
// It moves a published tender to open when its opening time passes, and an open one to closed when its
// closing time passes. Both are system transitions in the written process, so neither checks a permission.
//
// It runs every five minutes rather than daily, on the same durable recurring model as the other jobs.
// Tender deadlines are precise to the time of day, so a daily cadence would make "the window opened at nine"
// mean "up to a day late" for no reason.
//
//
// IDEMPOTENT BY CONSTRUCTION RATHER THAN BY A MARKER
//
// Each pass selects only tenders still in the source state whose threshold has passed. Once transitioned
// they no longer match, so a retry or an overlapping run cannot fire twice.
//
// No reason is recorded for these transitions. They are scheduled rather than an early close, and the
// domain only requires a reason for an early one.
//
//
// THE ANNOUNCEMENTS TRAVEL THE OUTBOX, NOT A SEPARATE QUEUE
//
// A clock-triggered transition is still a state change. The clock decided when; what is being announced is
// the state change, and it must not outlive a rollback, so the notifications are written in the same commit.
//
// Opening notifies the invitees. Closing notifies the invitees and the committee, which are the two groups
// the written process names.
//
//
// DRAFTS THAT SURVIVED THE WINDOW
//
// The rule that a draft lapses when the window closes is enforced here, for the first time. A draft that
// survived was previously left in draft forever: the supplier's dashboard kept counting a bid that could
// never be submitted, and nothing in the record said what had happened to it.
//
// The drafts are loaded BEFORE the transition, so the set is the one the closing applies to, and they are
// keyed by tender so each supplier is told about their own bid rather than about the tender.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

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

        var closingRfqIds = toClose.Select(r => r.Id).ToList();
        var lapsingDrafts = closingRfqIds.Count == 0
            ? []
            : await db.Proposals
                .Where(p => closingRfqIds.Contains(p.RfqId) && p.State == ProposalState.Draft)
                .ToListAsync(ct);

        foreach (var rfq in toClose)
        {
            rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);

            var closedRecipients = await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct);
            closedRecipients.AddRange(await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct));
            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqSubmissionClosed, closedRecipients,
                $"{NotificationTypes.RfqSubmissionClosed}:{rfq.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_closed", actorLabel: "system",
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.SubmissionOpen), toState: nameof(RfqState.SubmissionClosed), ct: ct);

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

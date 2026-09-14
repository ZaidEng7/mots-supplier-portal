// An officer sends a draft tender for internal approval.
//
// A nominated approver has to be able to approve. That is checked here rather than in the domain because it
// is a question about users, which are a different aggregate, and it is the same split the supplier
// eligibility rule already uses.
//
// It is refused rather than silently ignored. An officer who named a colleague and got the whole pool
// notified instead would have no way to tell.
//
// The new approval step is added to the tracked set explicitly, the same identifier gotcha every other child
// insert in this codebase has: without it the graph-tracking heuristic sees a set identifier, marks the row
// as existing, issues an update against a row that is not there yet, and the concurrency failure surfaces on
// the NEXT save that touches this tender.
//
// The notification is enqueued inside the transaction, so it cannot fire for a submission that rolled back.
// It goes to the approver this pass named, falling back to the pool when it named nobody.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class SubmitRfqForReviewHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitRfqForReviewHandler
{
    public async Task<RfqMutationResult> HandleAsync(SubmitRfqForReviewCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        if (command.AssignedApproverUserId is { } nominee
            && !await StaffEligibility.HoldsPermissionAsync(db, nominee, rfq.OrganizationId, Permissions.RfqApprove, ct))
        {
            return new RfqMutationResult.IneligibleUser(
                "The nominated approver is not an active user of this organization with permission to approve RFQs.");
        }

        try
        {
            rfq.SubmitForReview(command.AssignedApproverUserId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.InternalReview);
        }

        db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));

        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqSubmittedForReview,
            await NotificationRecipients.RfqApproverAsync(db, rfq, ct),
            $"{NotificationTypes.RfqSubmittedForReview}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submitted_for_review", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Draft), toState: nameof(RfqState.InternalReview), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

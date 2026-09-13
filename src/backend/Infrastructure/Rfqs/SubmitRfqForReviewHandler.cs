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

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: Draft -&gt; InternalReview.</summary>
public sealed class SubmitRfqForReviewHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitRfqForReviewHandler
{
    public async Task<RfqMutationResult> HandleAsync(SubmitRfqForReviewCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        // A-7: a nominated approver has to be able to approve. Checked here rather than in the domain
        // because it is a question about Users, a different aggregate - the same split BRULE-032
        // already uses for supplier eligibility. Refused rather than silently ignored: an officer who
        // named a colleague and got the whole pool notified would have no way to tell.
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

        // Same client-assigned-GUIDv7 gotcha as every other child Add in this codebase
        // (ManageContactHandler.cs's own comment): without this, EF's graph-tracking heuristic
        // sees a non-default Id on the new RfqApproval and marks it Modified instead of Added,
        // issuing an UPDATE against a row that does not exist yet - 0 rows affected -
        // DbUpdateConcurrencyException on the NEXT SaveChanges that touches this aggregate.
        db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));

        // §3.1 "Draft -> InternalReview | In-app to `procurement_manager`". Enqueued INSIDE the
        // transaction (D-5): a notification must not fire for a submission that rolled back.
        // A-7: the approver this pass was assigned to, falling back to the pool when it named nobody.
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

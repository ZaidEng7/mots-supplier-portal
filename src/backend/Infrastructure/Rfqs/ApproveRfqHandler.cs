// An approver approves a tender that was sent for internal review.
//
// One approver for now, which is the interim reading of an open question. The schema stores approvals as a
// sequence anyway, and the approval record's own header says why.
//
// The notification goes to the owner, who is the one who can now publish it.

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

public sealed class ApproveRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IApproveRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(ApproveRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.Approve(scope.UserId.Value);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Approved);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqApproved,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.RfqApproved}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_approved", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.InternalReview), toState: nameof(RfqState.Approved), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

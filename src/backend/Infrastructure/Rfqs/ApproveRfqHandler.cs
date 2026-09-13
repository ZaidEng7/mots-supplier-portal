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

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -&gt; Approved. OQ-004 interim
/// single-approver - see RfqApproval.cs's own doc comment for why the schema is an array anyway.</summary>
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

        // §3.1 "InternalReview -> Approved | In-app to officer" - A-7: the owner, who is the one
        // who can now publish it.
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

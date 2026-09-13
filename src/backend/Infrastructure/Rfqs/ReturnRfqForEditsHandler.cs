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

/// <summary>FEAT-07.4/BUSINESS-PROCESSES.md §3.1: InternalReview -&gt; Draft, "return for
/// edits".</summary>
public sealed class ReturnRfqForEditsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReturnRfqForEditsHandler
{
    public async Task<RfqMutationResult> HandleAsync(ReturnRfqForEditsCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.ReturnForEdits(scope.UserId.Value, command.Comments);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Draft);
        }

        // §3.1 "InternalReview -> Draft | In-app to officer" - A-7: the officer who OWNS it, so the
        // person who has to act on the comments is the person told about them.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqReturnedForEdits,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.RfqReturnedForEdits}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_returned", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.InternalReview), toState: nameof(RfqState.Draft), reason: command.Comments, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

// A buyer pauses evaluation to ask the bidders for clarification.
//
// The same shape as every other transition handler here: load within scope, call the aggregate, translate
// the two kinds of refusal, audit with the states it moved between, save.
//
// Deliberately not a new pattern. Three states becoming reachable is a gap in the machine rather than a
// reason to invent a second way of moving through it.
//
// The invitees are who a clarification is addressed to, and the notification catalogue is where the words
// live. The moment is part of the de-duplication key, because two clarification rounds on one tender are two
// pieces of news.

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

public sealed class RequestRfqClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IRequestRfqClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(RequestRfqClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RequestClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Clarification);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqClarificationRequested,
            await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
            $"{NotificationTypes.RfqClarificationRequested}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_requested", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.UnderEvaluation),
            toState: nameof(RfqState.Clarification), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

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

/// <summary>FEAT-07.6/BUSINESS-PROCESSES.md §3.1: manual early close of the submission window
/// (the scheduled deadline-driven close is RfqTimelineJob, a system actor, not this handler).</summary>
/// <summary>
/// T3-36. §3.1: "UnderEvaluation | Clarification | Request clarification |
/// `procurement_officer`,`evaluator` / `rfq.clarify` | Reason; targeted supplier(s)".
///
/// <para>Same shape as every other transition handler in this file - load scoped, call the
/// aggregate, catch the two refusal kinds, audit with from/to states, save. Deliberately not a new
/// pattern: three states becoming reachable is a gap in the machine, not a reason to invent a second
/// way of moving through it.</para>
/// </summary>
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

        // §3.1's notification column: "Email + in-app to targeted supplier". The invitees are who the
        // clarification is addressed to, and EPIC-15's catalogue is where the words live.
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

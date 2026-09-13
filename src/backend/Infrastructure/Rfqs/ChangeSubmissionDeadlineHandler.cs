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

/// <summary>
/// T-018/BRULE-035: <i>"Deadline extension while Published/SubmissionOpen: procurement_officer may
/// extend submissionCloseAt (audit rfq.deadline_extended, notify all invitees). Shortening the window
/// requires procurement_manager."</i>
/// </summary>
public sealed class ChangeSubmissionDeadlineHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IChangeSubmissionDeadlineHandler
{
    public async Task<RfqMutationResult> HandleAsync(ChangeSubmissionDeadlineCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        // BOTH permission checks live here, not on the route. The direction decides which one applies
        // and the direction is only knowable once the current deadline has been read - and the two
        // permissions belong to two different roles, so a single route filter would lock out whichever
        // caller it did not name. Checked BEFORE the mutation, so a refusal leaves the aggregate
        // untouched.
        var wouldShorten = rfq.SubmissionClosesAt is { } current && command.NewCloseAt < current;
        var required = wouldShorten ? Permissions.RfqDeadlineShorten : Permissions.RfqEdit;
        if (!scope.HasPermission(required))
        {
            return new RfqMutationResult.DeadlineChangeNotPermitted();
        }

        var previous = rfq.SubmissionClosesAt;
        bool shortened;
        try
        {
            shortened = rfq.ChangeSubmissionDeadline(command.NewCloseAt, command.Reason);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        // BRULE-035 names rfq.deadline_extended for the extension. The shortening gets its own action
        // rather than sharing it: an audit search for "who cut this tender short" must not have to
        // read two timestamps out of a row named "extended".
        //
        // D-12: there is no cap on an extension, so THIS ROW is the control. Both dates are recorded,
        // because "extended" without the from/to says nothing about by how much.
        // A-6: the reason joins the row. D-12 left the extension uncapped and called the audit row the
        // control; without a reason that row records only that someone moved a date, which is not a
        // control anyone can act on.
        await auditLogger.LogAsync("Rfq", rfq.Id,
            shortened ? "rfq.deadline_shortened" : "rfq.deadline_extended",
            scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: previous?.ToString("O"), toState: command.NewCloseAt.ToString("O"),
            reason: command.Reason, ct: ct);

        // "notify all invitees" - every invited supplier's users, not the committee. A deadline change
        // is only news to the people bidding against it.
        NotificationOutbox.EnqueueMany(db,
            shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended,
            await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
            // The new date is in the dedupe key: two successive changes are two pieces of news, and a
            // key on the RFQ alone would silently swallow the second.
            $"{(shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended)}:{rfq.Id}:{command.NewCloseAt:O}",
            // No date in the payload. BRULE-091's allow-list treats a date as content, and the copy
            // points at the RFQ instead - see NotificationPayload.AllowedKeys.
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode });

        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

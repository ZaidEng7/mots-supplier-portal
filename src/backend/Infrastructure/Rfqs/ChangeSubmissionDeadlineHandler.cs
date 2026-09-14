// Moving a tender's submission deadline, in either direction.
//
// The written rule permits an officer to extend the closing time while the tender is published or open, and
// requires a manager to shorten it.
//
//
// BOTH PERMISSION CHECKS LIVE HERE, NOT ON THE ROUTE
//
// The direction decides which permission applies, and the direction is only knowable once the current
// deadline has been read. The two permissions also belong to two different roles, so a single route filter
// would lock out whichever caller it did not name.
//
// Checked before the mutation, so a refusal leaves the tender untouched.
//
//
// SHORTENING GETS ITS OWN AUDIT ACTION
//
// The rule names one action for the extension. Shortening does not share it: an audit search for "who cut
// this tender short" must not have to read two timestamps out of a row named "extended".
//
// There is no cap on an extension, by a recorded decision, which makes THIS ROW the control. Both dates are
// recorded, because "extended" without the from and the to says nothing about by how much. And the caller's
// reason joins the row, because without it the row records only that somebody moved a date, which is not a
// control anyone can act on.
//
//
// WHO IS TOLD, AND WHAT THE MESSAGE CARRIES
//
// Every invited supplier's users, and not the committee. A deadline change is only news to the people
// bidding against it.
//
// The new date is part of the de-duplication key, because two successive changes are two pieces of news and
// a key on the tender alone would silently swallow the second.
//
// The date is not in the payload. The rule's allow-list treats a date as content, so the copy points at the
// tender instead.

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

public sealed class ChangeSubmissionDeadlineHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IChangeSubmissionDeadlineHandler
{
    public async Task<RfqMutationResult> HandleAsync(ChangeSubmissionDeadlineCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

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

        await auditLogger.LogAsync("Rfq", rfq.Id,
            shortened ? "rfq.deadline_shortened" : "rfq.deadline_extended",
            scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: previous?.ToString("O"), toState: command.NewCloseAt.ToString("O"),
            reason: command.Reason, ct: ct);

        NotificationOutbox.EnqueueMany(db,
            shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended,
            await NotificationRecipients.RfqInviteeUsersAsync(db, rfq.Id, ct),
            $"{(shortened ? NotificationTypes.RfqDeadlineShortened : NotificationTypes.RfqDeadlineExtended)}:{rfq.Id}:{command.NewCloseAt:O}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode });

        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

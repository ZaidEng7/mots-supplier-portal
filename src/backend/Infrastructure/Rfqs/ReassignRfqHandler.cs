// Handing a tender to another officer.
//
//
// NOT A STATE TRANSITION, AND DELIBERATELY NOT GATED LIKE ONE
//
// Ownership can move at any point in a live tender: somebody leaves, somebody is on holiday, a workload is
// rebalanced. So this is permitted from every state except the two terminal ones, where no action remains
// for anyone to own, and the domain is what refuses those.
//
//
// BEHIND A PERMISSION MANAGERS HOLD AND OFFICERS DO NOT
//
// An officer reassigning their own work away is how accountability gets quietly dropped. Ownership exists
// to make somebody answerable, and letting the answerable party choose to stop being answerable would undo
// it.
//
// An officer who cannot continue asks their manager, which is a conversation the audit row then records.
//
// The nominated owner must be able to do an owner's job, so the check is for the edit permission rather than
// for a role name and rather than for read access. An owner who can see the tender but not change it is an
// owner in name.
//
//
// A REFUSAL FROM THE DOMAIN IS ABOUT THE REQUEST, NOT THE MACHINE
//
// The state does not change here, so an answer quoting the allowed next states would describe a machine this
// operation does not move.
//
//
// THE AUDIT ROW IS THE POINT OF THE OPERATION
//
// Both identifiers go in the structured change rather than into the free-text reason, so "who used to own
// this" is queryable rather than parseable, and the reason column carries the human explanation the caller
// had to supply.
//
// The new owner is told. Without that the reassignment is a fact recorded about somebody who does not know
// it, and every later "notify the officer" would arrive without context.

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

public sealed class ReassignRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReassignRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(ReassignRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        if (!await StaffEligibility.HoldsPermissionAsync(db, command.NewOwnerUserId, rfq.OrganizationId, Permissions.RfqEdit, ct))
        {
            return new RfqMutationResult.IneligibleUser(
                "The nominated owner is not an active user of this organization with permission to work on RFQs.");
        }

        var previousOwnerUserId = rfq.OwnerUserId;

        try
        {
            rfq.Reassign(command.NewOwnerUserId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_reassigned", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason,
            changes: JsonSerializer.Serialize(new
            {
                fromOwnerUserId = previousOwnerUserId,
                toOwnerUserId = command.NewOwnerUserId,
            }), ct: ct);

        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqReassigned,
            [command.NewOwnerUserId],
            $"{NotificationTypes.RfqReassigned}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

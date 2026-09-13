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
/// A-7: hand an RFQ to another officer.
///
/// <para><b>Not a state transition, and deliberately not gated like one.</b> Ownership can move at any
/// point in a live tender - somebody leaves, somebody is on holiday, a workload is rebalanced - so this
/// is permitted from every non-closed state rather than only from Draft. The domain refuses the two
/// terminal states, where no action remains for anyone to own.</para>
///
/// <para><b>Behind <c>rfq.reassign</c>, which managers hold and officers do not.</b> An officer
/// reassigning their own work away is how accountability gets quietly dropped; A-7 exists to make
/// somebody answerable, and letting the answerable party choose to stop being answerable would undo
/// it. An officer who cannot continue asks their manager, which is a conversation the audit row then
/// records.</para>
/// </summary>
public sealed class ReassignRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReassignRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(ReassignRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        // The new owner must be able to do an owner's job. `rfq.edit` rather than a role name, and
        // rather than `rfq.read`: an owner who can see the RFQ but not change it is an owner in name.
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
            // Not an illegal TRANSITION - the state does not change - so a 409 quoting an allowed-next
            // set would describe a machine this operation does not move. A 400 about the request.
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        // The audit row is the point of this operation. Both ids are in `changes` rather than in the
        // free-text reason, so "who used to own this" is queryable rather than parseable - and the
        // reason column carries the human explanation the caller had to supply.
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_reassigned", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason,
            changes: JsonSerializer.Serialize(new
            {
                fromOwnerUserId = previousOwnerUserId,
                toOwnerUserId = command.NewOwnerUserId,
            }), ct: ct);

        // The new owner is told. Without this the reassignment is a fact recorded about someone who
        // does not know it, and every subsequent "notify the officer" would arrive without context.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqReassigned,
            [command.NewOwnerUserId],
            $"{NotificationTypes.RfqReassigned}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-08.6/FR-INV-006: the supplier-facing self-service side of RFQ Invitations - the
/// security boundary this feature exists for. Every handler here resolves the RFQ by
/// (SupplierId, ReferenceCode) through a real Invitation row, never by ReferenceCode alone: a
/// non-invited supplier's request finds no row and gets the same NotFoundOrNotInvited a wrong
/// reference code would, so the two cases are indistinguishable from outside (no oracle for
/// "does this RFQ exist").</summary>
file static class SupplierRfqLoader
{
    /// <summary>Also requires the RFQ to be Published or later - Draft/InternalReview/Approved
    /// RFQs are buyer-internal even for an already-invited supplier (invitations can be created
    /// starting in Draft per FEAT-08.1/candidate-identification, but visibility only opens at
    /// Publish, matching BUSINESS-PROCESSES.md's "Approved -&gt; Published: generate access").</summary>
    public static async Task<(Rfq Rfq, Invitation Invitation)?> LoadInvitedAsync(
        AppDbContext db, IScopeContext scope, string referenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var rfq = await db.Rfqs
            .Include(r => r.Items).Include(r => r.Requirements).Include(r => r.Attachments).Include(r => r.Invitations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode, ct);
        if (rfq is null || rfq.State is RfqState.Draft or RfqState.InternalReview or RfqState.Approved) return null;

        var invitation = rfq.Invitations.FirstOrDefault(i => i.SupplierId == scope.SupplierId.Value);
        return invitation is null ? null : (rfq, invitation);
    }
}

public sealed class SupplierListInvitedRfqsHandler(AppDbContext db, IScopeContext scope) : ISupplierListInvitedRfqsHandler
{
    public async Task<IReadOnlyList<SupplierRfqDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];

        var rfqs = await db.Rfqs
            .Include(r => r.Items).Include(r => r.Requirements).Include(r => r.Attachments).Include(r => r.Invitations)
            .AsSplitQuery()
            .Where(r => r.Invitations.Any(i => i.SupplierId == scope.SupplierId)
                && r.State != RfqState.Draft && r.State != RfqState.InternalReview && r.State != RfqState.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return [.. rfqs.Select(r => RfqDtoMapper.ToSupplierDto(r, r.Invitations.Single(i => i.SupplierId == scope.SupplierId)))];
    }
}

/// <summary>Marks the invitation Viewed as a side effect of a successful fetch (FEAT-08.6) - the
/// first time an invited supplier actually opens the RFQ, not merely lists it.</summary>
public sealed class SupplierGetRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISupplierGetRfqHandler
{
    public async Task<SupplierRfqResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, referenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        var wasFirstView = invitation.Status == InvitationStatus.Invited;
        rfq.MarkInvitationViewed(scope.SupplierId!.Value);
        if (wasFirstView)
        {
            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_viewed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
        }

        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation));
    }
}

public sealed class SupplierDeclineInvitationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ISupplierDeclineInvitationHandler
{
    public async Task<SupplierRfqResult> HandleAsync(DeclineInvitationCommand command, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, command.ReferenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        try
        {
            rfq.DeclineInvitation(scope.SupplierId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new SupplierRfqResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_declined", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation));
    }
}

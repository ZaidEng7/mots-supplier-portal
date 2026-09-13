using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>Resolves (Rfq, Invitation, Proposal?) for the caller's own SupplierId - reuses
/// SupplierRfqLoader.LoadInvitedAsync (EPIC-08) for the invitation check rather than
/// reimplementing it, per this epic's own instruction.</summary>
internal static class ProposalLoader
{
    public static async Task<(Rfq Rfq, Proposal? Proposal)?> LoadAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;
        var (rfq, _) = loaded.Value;

        // A supplier may now have more than one proposal on an RFQ: BUSINESS-PROCESSES.md §4.1
        // permits re-entry after a withdrawal, and names its mechanism as a NEW DRAFT. So the LIVE
        // proposal wins over a withdrawn one, and a withdrawn one is still returned when it is all
        // there is - a supplier who withdrew and has not restarted should still see that they
        // withdrew, rather than a screen that behaves as though they were never here.
        var proposal = await db.Proposals
            .Include(p => p.Items).Include(p => p.Documents).Include(p => p.RequirementAnswers)
            .AsSplitQuery()
            .Where(p => p.RfqId == rfq.Id && p.SupplierId == scope.SupplierId!.Value)
            .OrderBy(p => p.State == ProposalState.Withdrawn ? 1 : 0)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return (rfq, proposal);
    }

    /// <summary>
    /// §12-A/C2: resolve a proposal by its OWN public code, for the relocated
    /// <c>/api/v1/proposals/{proposalCode}</c> routes (§3: <c>/proposals/{proposalCode}/items</c>,
    /// §12.5: <c>PATCH /proposals/{proposalCode}</c>).
    ///
    /// <para><b>The row-scope predicate is the whole point.</b> The old path could not name another
    /// supplier's proposal - <c>/suppliers/me/rfqs/{code}/proposal</c> has no slot for one. This one
    /// can, so <c>SupplierId == scope.SupplierId</c> is applied IN THE QUERY rather than checked
    /// afterwards, and a miss is indistinguishable from a code that never existed. §9.2:
    /// *"Out-of-scope access to an existing resource returns 404 (not 403) to avoid leaking
    /// existence"* - so this returns null for both cases and the endpoint maps null to 404.</para>
    /// </summary>
    public static async Task<(Rfq Rfq, Proposal Proposal)?> LoadByProposalCodeAsync(
        AppDbContext db, IScopeContext scope, string proposalReferenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var proposal = await db.Proposals
            .Include(p => p.Items).Include(p => p.Documents).Include(p => p.RequirementAnswers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                p => p.ReferenceCode == proposalReferenceCode && p.SupplierId == scope.SupplierId!.Value, ct);
        if (proposal is null) return null;

        // Items and Requirements are NOT optional here. Submit-completeness asks "is every required
        // RFQ item priced, is every mandatory requirement answered" by walking these collections,
        // so loading the RFQ bare makes both checks vacuously TRUE and lets an incomplete proposal
        // submit. The RFQ-keyed loader this replaces got them from SupplierRfqLoader's includes;
        // dropping them here was silent, and two existing completeness tests caught it.
        var rfq = await db.Rfqs
            .Include(r => r.Items)
            .Include(r => r.Requirements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == proposal.RfqId, ct);
        return rfq is null ? null : (rfq, proposal);
    }
}

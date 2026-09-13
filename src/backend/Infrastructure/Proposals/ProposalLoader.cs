// The two ways a bid is loaded for its own supplier.
//
// Both reuse the invitation check the tender side already has rather than reimplementing it.
//
//
// A SUPPLIER MAY HAVE MORE THAN ONE BID ON A TENDER
//
// The written process permits re-entry after a withdrawal and names a new draft as the mechanism.
//
// So the live bid wins over a withdrawn one, and a withdrawn one is still returned when it is all there is. A
// supplier who withdrew and has not restarted should still see that they withdrew, rather than a screen that
// behaves as though they were never here.
//
//
// LOADING BY THE BID'S OWN CODE NEEDS THE SCOPE INSIDE THE QUERY
//
// The older route could not name another supplier's bid, because its path had no slot for one. This one can,
// so the ownership test is part of the query rather than a check afterwards, and a miss is indistinguishable
// from a code that never existed.
//
// That is the contract's rule: out-of-scope access to an existing resource answers not-found rather than
// forbidden, so existence is not leaked.
//
//
// AND IT NEEDS THE TENDER'S LINES AND REQUIREMENTS
//
// They are not optional. Submission completeness asks whether every required line is priced and every
// mandatory requirement answered by walking those collections, so loading the tender bare makes both checks
// vacuously true and lets an incomplete bid submit.
//
// The older loader got them from the tender side's includes. Dropping them here was silent, and two existing
// completeness tests caught it.

namespace MotsSupplierPortal.Infrastructure.Proposals;

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

internal static class ProposalLoader
{
    public static async Task<(Rfq Rfq, Proposal? Proposal)?> LoadAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;
        var (rfq, _) = loaded.Value;

        var proposal = await db.Proposals
            .Include(p => p.Items).Include(p => p.Documents).Include(p => p.RequirementAnswers)
            .AsSplitQuery()
            .Where(p => p.RfqId == rfq.Id && p.SupplierId == scope.SupplierId!.Value)
            .OrderBy(p => p.State == ProposalState.Withdrawn ? 1 : 0)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return (rfq, proposal);
    }

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

        var rfq = await db.Rfqs
            .Include(r => r.Items)
            .Include(r => r.Requirements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == proposal.RfqId, ct);
        return rfq is null ? null : (rfq, proposal);
    }
}

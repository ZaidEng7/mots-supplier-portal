using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-08.6/FR-INV-006: the supplier-facing self-service side of RFQ Invitations - the
/// security boundary this feature exists for. Every handler here resolves the RFQ by
/// (SupplierId, ReferenceCode) through a real Invitation row, never by ReferenceCode alone: a
/// non-invited supplier's request finds no row and gets the same NotFoundOrNotInvited a wrong
/// reference code would, so the two cases are indistinguishable from outside (no oracle for
/// "does this RFQ exist").</summary>
/// <summary>Internal (not file-scoped) so EPIC-09's ProposalHandlers.cs can reuse LoadInvitedAsync
/// for "is this caller invited to this RFQ" rather than reimplementing the same check.</summary>
internal static class SupplierRfqLoader
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
            .Include(r => r.Clarifications).Include(r => r.Addenda)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode, ct);
        if (rfq is null || rfq.State is RfqState.Draft or RfqState.InternalReview or RfqState.Approved) return null;

        var invitation = rfq.Invitations.FirstOrDefault(i => i.SupplierId == scope.SupplierId.Value);
        return invitation is null ? null : (rfq, invitation);
    }
}

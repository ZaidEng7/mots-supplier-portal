// The security boundary for every supplier-facing tender read.
//
// The tender is resolved by the caller's own supplier and the reference code together, through a real
// invitation row, and never by the reference code alone.
//
// So a supplier who was not invited finds no row and gets the same answer a wrong reference code would.
// The two cases are indistinguishable from outside, which means this endpoint is no oracle for whether a
// tender exists.
//
// It also requires the tender to be published or later. A draft, one in internal review, or an approved but
// unpublished one is buyer-internal even to an already-invited supplier: invitations can be created while
// the tender is still a draft, but visibility only opens at publication, which is what the written process
// means by generating access at that point.
//
// Internal rather than private to this file so the bid handlers can reuse it for "is this caller invited to
// this tender" rather than reimplementing the same check.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class SupplierRfqLoader
{
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

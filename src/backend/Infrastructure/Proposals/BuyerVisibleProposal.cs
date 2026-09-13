using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>The buyer-side visibility predicate, written once so the list and the download cannot
/// drift apart - a list that shows a file the download refuses is a bug report, and a download that
/// serves a file the list hides is a leak.</summary>
internal static class BuyerVisibleProposal
{
    public static async Task<Proposal?> LoadAsync(
        AppDbContext db, IScopeContext scope, string rfqReferenceCode, Guid proposalId, CancellationToken ct)
    {
        // A supplier never reaches the buyer surface, even one who happens to hold the permission.
        if (scope.SupplierId is not null) return null;

        var rfq = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rfq is null) return null;

        var evaluationState = await db.Evaluations.AsNoTracking()
            .Where(e => e.RfqId == rfq.Id)
            .Select(e => (EvaluationState?)e.State)
            .FirstOrDefaultAsync(ct);

        // The gate. Null covers "no evaluation has been opened", which is emphatically before
        // consolidation rather than a special case that skips it.
        if (evaluationState is not (EvaluationState.Consolidated or EvaluationState.Finalized)) return null;

        return await db.Proposals
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.RfqId == rfq.Id, ct);
    }
}

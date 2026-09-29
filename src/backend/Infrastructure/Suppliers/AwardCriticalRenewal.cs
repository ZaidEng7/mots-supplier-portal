// Whether a supplier still has an award-critical document that expired and has no approved renewal.
//
// ONE RULE FOR BOTH THINGS THAT BRING A SUPPLIER BACK. AutomaticReinstatement lifts a suspension the expiry rule made,
// and the ERP sync lifts one it made while Seven Gates approved the supplier; neither may bring back a supplier with an
// expired document. Two copies of "renewed" would drift, and the first copy already had once - it read a replacement
// still waiting for review as a renewal. So "renewed" means what AutomaticReinstatement's header says: every
// award-critical type that has an expired version also has an approved latest one.
//
// THE SYNC ASKS FOR EVERY SUPPLIER AT ONCE when it previews, and for one supplier when it runs, so both reads are
// here. They read without tracking; AutomaticReinstatement keeps its own tracked read, because the document a reviewer
// is approving in that same unit of work must count as approved there.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class AwardCriticalRenewal
{
    public static bool NotYetRenewed(IEnumerable<(Guid TypeId, DocumentState State, bool IsLatest)> awardCritical)
    {
        var documents = awardCritical.ToList();

        return documents
            .Where(d => d.State == DocumentState.Expired)
            .Select(d => d.TypeId)
            .Distinct()
            .Any(typeId => !documents.Any(d =>
                d.TypeId == typeId && d.IsLatest && d.State is (DocumentState.Approved or DocumentState.ExpiringSoon)));
    }

    public static async Task<bool> AwaitsRenewalAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        (await SuppliersAwaitingRenewalAsync(db, [supplierId], ct)).Contains(supplierId);

    public static async Task<HashSet<Guid>> SuppliersAwaitingRenewalAsync(
        AppDbContext db, IReadOnlyCollection<Guid>? supplierIds, CancellationToken ct)
    {
        var awardCriticalTypeIds = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.IsAwardCritical)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var documents = await db.SupplierDocuments.AsNoTracking()
            .Where(d => awardCriticalTypeIds.Contains(d.DocumentTypeId)
                && (supplierIds == null || supplierIds.Contains(d.SupplierId)))
            .Select(d => new { d.SupplierId, d.DocumentTypeId, d.State, d.IsLatestVersion })
            .ToListAsync(ct);

        return [.. documents
            .GroupBy(d => d.SupplierId)
            .Where(g => NotYetRenewed(g.Select(d => (d.DocumentTypeId, d.State, d.IsLatestVersion))))
            .Select(g => g.Key)];
    }
}

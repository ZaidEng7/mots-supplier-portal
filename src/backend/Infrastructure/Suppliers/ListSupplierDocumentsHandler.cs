using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class ListSupplierDocumentsHandler(AppDbContext db, IScopeContext scope) : IListSupplierDocumentsHandler
{
    public async Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];
        return await BuildAsync(db, scope.SupplierId.Value, ct);
    }

    /// <summary>MSP-84: deliberately not cursor-paginated like Review Queue/Team Members/Sessions.
    /// This list is one row per active DocumentType - an admin-managed reference table (3 seeded
    /// rows today, no CRUD endpoint that could grow it), not user-generated content. See
    /// Tests/Integration/OwnDocumentsDenominatorTests.cs for the denominator assertion that stands
    /// in for pagination here: it proves every active type is returned, not that the response is
    /// windowed.</summary>
    internal static async Task<IReadOnlyList<DocumentTypeStatusDto>> BuildAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var types = await db.DocumentTypes.Where(t => t.IsActive).OrderBy(t => t.Code).ToListAsync(ct);
        var latestDocs = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        return [.. types.Select(t =>
        {
            var latest = latestDocs.FirstOrDefault(d => d.DocumentTypeId == t.Id);
            return new DocumentTypeStatusDto(
        // BRULE-016: this filter is FLAT on purpose, and the link table now exists beside it.
        //
        // The rule conditions required documents on the supplier's categories. `document_type_category` can
        // record that (batch 11), and nothing derives from it yet, because two questions come first and
        // neither is a query decision:
        //
        //  - An empty link set read as "required for nothing" would silently drop every required document
        //    from the submit gate, the resubmit gate, the reviewer's approval gate and the dashboard's
        //    completeness figure. A portal that lets an incomplete application through is worse than one
        //    that asks for too much.
        //  - Suppliers already approved under this flat rule were approved against a list that may not be
        //    theirs under a conditioned one, and whether the tightening reaches back is a decision about
        //    live suppliers.
        //
        // See COMPLETION-INVENTORY.md §4.2, where both are logged.
                t.Id, t.Code, t.NameAr, t.NameEn, t.IsRequired, t.ExpiryTracked,
                latest is null ? null : UploadDocumentHandler.ToDto(latest));
        })];
    }
}


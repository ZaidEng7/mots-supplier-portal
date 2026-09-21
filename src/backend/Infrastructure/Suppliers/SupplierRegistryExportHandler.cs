// Loads the whole supplier registry for the CSV export, one supplier at a time.
//
// EVERY SUPPLIER IS INCLUDED, at every onboarding state, not only approved ones. The data lake is being built to
// answer questions about the registry, and "how many applications are still in progress" is one of them. The
// export's provenance header says so, because a file called suppliers that quietly meant approved suppliers
// would be counted as the whole registry by whoever loads it.
//
// ORDERED BY ReferenceCode so two runs a minute apart produce the same rows in the same order. An unordered
// export diffs as though everything changed.
//
// AsSplitQuery, because a supplier is joined to six child collections and a single query would multiply them
// together - the classic cartesian explosion that turns 38 suppliers into tens of thousands of rows on the wire.
//
// PROFILE COMPLETENESS COSTS TWO QUERIES PER SUPPLIER and is computed here anyway, by calling the same evaluator
// the profile screen calls. The rule is "which required document types is this supplier missing", required types
// vary per supplier by category, and a second copy of that rule written for the export would drift from the one
// users see - a supplier would read 80% on screen and 60% in the lake, and nobody would know which was wrong.
// At the registry's present size that is fewer than a hundred small queries. If the registry reaches a scale
// where this matters, the fix is to bulk-load the document-type mapping once and lift the rule into a pure
// function both callers use - not to fork it.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierRegistryExportHandler(AppDbContext db) : ISupplierRegistryExportHandler
{
    public async Task<SupplierExportLookups> GetLookupsAsync(CancellationToken ct)
    {
        var documentTypes = await db.DocumentTypes
            .AsNoTracking()
            .Select(t => new { t.Id, t.Code })
            .ToDictionaryAsync(t => t.Id, t => t.Code, ct);

        var categories = await db.Categories
            .AsNoTracking()
            .Select(c => new { c.Code, c.NameEn, c.NameAr })
            .ToListAsync(ct);

        var regions = await db.Regions
            .AsNoTracking()
            .Select(r => new { r.Code, r.NameEn })
            .ToDictionaryAsync(r => r.Code, r => r.NameEn, ct);

        return new SupplierExportLookups(
            documentTypes,
            categories.ToDictionary(c => c.Code, c => c.NameEn),
            categories.ToDictionary(c => c.Code, c => c.NameAr),
            regions);
    }

    public async IAsyncEnumerable<SupplierExportRecord> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var suppliers = db.Suppliers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Representatives)
            .Include(s => s.Addresses)
            .Include(s => s.Contacts)
            .Include(s => s.Branches)
            .Include(s => s.BankAccounts)
            .Include(s => s.CategoryLinks)
            .OrderBy(s => s.ReferenceCode)
            .AsAsyncEnumerable();

        await foreach (var supplier in suppliers.WithCancellation(ct))
        {
            var documents = await db.SupplierDocuments
                .AsNoTracking()
                .Where(d => d.SupplierId == supplier.Id && d.IsLatestVersion)
                .ToListAsync(ct);

            var offerings = await db.Offerings
                .AsNoTracking()
                .Where(o => o.SupplierId == supplier.Id)
                .ToListAsync(ct);

            var annotations = await db.SupplierReviewAnnotations
                .AsNoTracking()
                .Where(a => a.SupplierId == supplier.Id)
                .ToListAsync(ct);

            var missingDocumentTypes = await DocumentCompletenessEvaluator
                .GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);

            var requiredDocumentTypeCount = (await RequiredDocumentTypeResolver
                .ForSupplierAsync(db, supplier.Id, ct)).Count;

            var completeness = Application.Suppliers.ProfileCompleteness.Ratio(
                missingItems: supplier.GetMissingProfileFields().Count + missingDocumentTypes.Count,
                totalItems: Supplier.RequiredProfileFieldCodes.Count + requiredDocumentTypeCount);

            yield return new SupplierExportRecord(
                supplier, documents, offerings, annotations, completeness, missingDocumentTypes);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// BRULE-016: which documents THIS supplier is required to hold, given what it sells.
///
/// <para><b>The one place the condition lives.</b> Four sites derived the required set independently and
/// identically - the submit gate, the resubmit gate and the reviewer's approval gate through
/// <see cref="DocumentCompletenessEvaluator"/>, plus <c>GetSupplierHandler</c>'s completeness fraction,
/// <c>SupplierDashboardHandler</c>'s denominator and <c>ListSupplierDocumentsHandler</c>'s checklist. Each
/// carried the same paragraph explaining why it was flat. Conditioning them one at a time is how a
/// supplier is told on the dashboard that they need a document the submit gate does not ask for, so the
/// derivation is now a single function and the call sites ask it rather than repeating it.</para>
///
/// <para><b>The rule.</b> A required type with no category links is required of everyone; a required type
/// WITH links is required only of suppliers holding one of those categories. That asymmetry is the answer
/// to the question that kept this switched off: an empty link table must not mean "required for nothing",
/// because that would silently empty every gate in the product, and a portal that lets an incomplete
/// application through is worse than one that asks for too much. Unlinked therefore means unconditioned -
/// the same behaviour the flat rule had - and recording a link is what narrows a type to an audience.</para>
///
/// <para><b>A supplier with no categories</b> is asked for the unlinked types only. That is the same
/// reading: nothing about what they sell is known, so nothing category-conditioned can be demanded of
/// them. It is also self-correcting, because the categories are captured during registration and the
/// submit gate runs after them.</para>
///
/// <para><b>Retroactive, per D-59.</b> This applies to every supplier rather than only to new
/// registrations - including suppliers already approved under the flat set. D-59 records why, and records
/// that it is a one-way door: an approval made under one required set is not re-made by changing the set.
/// It is safe now because every supplier in the system is demonstration data.</para>
/// </summary>
public static class RequiredDocumentTypeResolver
{
    /// <summary>The active, required document types this supplier must hold.</summary>
    public static async Task<IReadOnlyList<DocumentType>> ForSupplierAsync(
        AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).ToListAsync(ct);
        if (requiredTypes.Count == 0) return [];

        return await NarrowAsync(db, supplierId, requiredTypes, ct);
    }

    /// <summary>
    /// The ids of the required types for this supplier, out of a set the caller has already loaded.
    ///
    /// <para>For the document checklist, which lists every ACTIVE type and needs to say which of them are
    /// required of this supplier - a different question from "give me the required ones", and one a second
    /// query would answer inconsistently the moment the two drifted.</para>
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> RequiredIdsAmongAsync(
        AppDbContext db, Guid supplierId, IReadOnlyCollection<DocumentType> activeTypes, CancellationToken ct)
    {
        var requiredTypes = activeTypes.Where(t => t.IsRequired && t.IsActive).ToList();
        if (requiredTypes.Count == 0) return new HashSet<Guid>();

        var narrowed = await NarrowAsync(db, supplierId, requiredTypes, ct);
        return narrowed.Select(t => t.Id).ToHashSet();
    }

    private static async Task<IReadOnlyList<DocumentType>> NarrowAsync(
        AppDbContext db, Guid supplierId, IReadOnlyList<DocumentType> requiredTypes, CancellationToken ct)
    {
        var typeIds = requiredTypes.Select(t => t.Id).ToList();

        var links = await db.DocumentTypeCategories.AsNoTracking()
            .Where(l => typeIds.Contains(l.DocumentTypeId))
            .Select(l => new { l.DocumentTypeId, l.CategoryCode })
            .ToListAsync(ct);

        // No link on any required type: nobody has narrowed anything, so the answer is the flat set and
        // the supplier's own categories need not be read at all.
        if (links.Count == 0) return requiredTypes;

        var supplierCategories = await db.CategoryLinks.AsNoTracking()
            .Where(l => l.SupplierId == supplierId)
            .Select(l => l.CategoryCode)
            .ToListAsync(ct);

        return
        [
            .. requiredTypes.Where(type =>
            {
                var linkedCategories = links.Where(l => l.DocumentTypeId == type.Id).Select(l => l.CategoryCode).ToList();
                return linkedCategories.Count == 0
                    || linkedCategories.Any(supplierCategories.Contains);
            })
        ];
    }
}

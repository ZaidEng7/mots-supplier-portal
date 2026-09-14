// Which documents THIS supplier is required to hold, given what it sells.
//
//
// THE ONE PLACE THE CONDITION LIVES
//
// Six places derived the required set independently and identically: the submit gate, the resubmit gate and
// the reviewer's approval gate through the completeness evaluator, plus the profile's completeness
// fraction, the dashboard's denominator and the document checklist. Each carried the same paragraph
// explaining why it was flat.
//
// Narrowing them one at a time is how a supplier gets told on the dashboard that they need a document the
// submit gate does not ask for. So the derivation is one function and the call sites ask it rather than
// repeating it.
//
//
// THE RULE
//
// A required type with no category links is required of everyone. A required type WITH links is required
// only of suppliers holding one of those categories.
//
// That asymmetry is the answer to the question that kept this switched off. An empty link table must not
// mean "required of nobody", because that would silently empty every gate in the product, and a portal
// that lets an incomplete application through is worse than one that asks for too much. Unlinked therefore
// means unconditioned, which is the behaviour the flat rule had, and recording a link is what narrows a
// type to an audience.
//
// A supplier with no categories is asked for the unlinked types only. Same reading: nothing about what
// they sell is known, so nothing category-conditioned can be demanded of them. It is also self-correcting,
// because the categories are captured during registration and the submit gate runs after them.
//
//
// IT APPLIES RETROACTIVELY, AND THAT IS A ONE-WAY DOOR
//
// This applies to every supplier rather than only to new registrations, including suppliers already
// approved under the flat set. The recorded decision says why, and says that it is one-way: an approval
// made under one required set is not re-made by changing the set. It is safe now because every supplier in
// the system is demonstration data.
//
//
// TWO QUESTIONS, NOT ONE
//
// One caller wants the required types. The checklist wants to list every active type and say which of them
// are required of this supplier. A second query answering the second question would disagree with the
// first the moment the two drifted, so both narrow through the same private step.
//
// When no required type carries a link, nobody has narrowed anything, the answer is the flat set, and the
// supplier's own categories need not be read at all.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class RequiredDocumentTypeResolver
{
    public static async Task<IReadOnlyList<DocumentType>> ForSupplierAsync(
        AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var requiredTypes = await db.DocumentTypes.Where(t => t.IsRequired && t.IsActive).ToListAsync(ct);
        if (requiredTypes.Count == 0) return [];

        return await NarrowAsync(db, supplierId, requiredTypes, ct);
    }

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

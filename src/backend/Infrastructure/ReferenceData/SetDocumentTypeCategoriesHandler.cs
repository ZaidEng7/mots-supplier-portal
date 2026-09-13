// Replacing the whole set of categories one document type is required for.
//
// Whole-set rather than add and remove. The thing an administrator decides is "this document is required for these
// categories", which is a set; separate endpoints would make one decision take several requests and leave a
// half-applied state visible in between.
//
// The categories are validated against the category table, and that is the check that matters: a link to a category
// that does not exist is a requirement no supplier can ever match, and it would stay invisible until the narrowing
// is switched on, at which point it silently excludes a document from everyone.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SetDocumentTypeCategoriesHandler(AppDbContext db) : ISetDocumentTypeCategoriesHandler
{
    public async Task<SetDocumentTypeCategoriesResult> HandleAsync(
        SetDocumentTypeCategoriesCommand command, CancellationToken ct)
    {
        var documentType = await db.Set<DocumentType>()
            .FirstOrDefaultAsync(t => t.Code == command.DocumentTypeCode, ct);
        if (documentType is null) return new SetDocumentTypeCategoriesResult.UnknownDocumentType();

        var requested = command.CategoryCodes.Distinct(StringComparer.Ordinal).ToList();

        var known = await db.Set<Category>().AsNoTracking()
            .Where(c => requested.Contains(c.Code))
            .Select(c => c.Code)
            .ToListAsync(ct);

        var unknown = requested.Except(known, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0) return new SetDocumentTypeCategoriesResult.UnknownCategories(unknown);

        var existing = await db.DocumentTypeCategories
            .Where(l => l.DocumentTypeId == documentType.Id)
            .ToListAsync(ct);

        db.DocumentTypeCategories.RemoveRange(existing.Where(l => !requested.Contains(l.CategoryCode, StringComparer.Ordinal)));

        foreach (var code in requested.Where(code => !existing.Any(l => l.CategoryCode == code)))
        {
            db.DocumentTypeCategories.Add(new DocumentTypeCategory
            {
                Id = Guid.CreateVersion7(),
                DocumentTypeId = documentType.Id,
                CategoryCode = code,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        return new SetDocumentTypeCategoriesResult.Success(
            new DocumentTypeCategoryLinksDto(documentType.Code, requested.OrderBy(c => c, StringComparer.Ordinal).ToList()));
    }
}

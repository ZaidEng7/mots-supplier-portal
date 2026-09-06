using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

public sealed class GetDocumentTypeCategoriesHandler(AppDbContext db) : IGetDocumentTypeCategoriesHandler
{
    public async Task<IReadOnlyList<DocumentTypeCategoryLinksDto>> HandleAsync(CancellationToken ct)
    {
        var links = await db.DocumentTypeCategories.AsNoTracking()
            .Join(db.Set<DocumentType>().AsNoTracking(), l => l.DocumentTypeId, t => t.Id,
                (l, t) => new { t.Code, l.CategoryCode })
            .ToListAsync(ct);

        // Driven by the document types, not by the link rows, so a type with no links still appears - an
        // administrator has to be able to see which types they have not classified yet, and a link-driven list
        // would hide exactly those.
        var types = await db.Set<DocumentType>().AsNoTracking().Select(t => t.Code).OrderBy(c => c).ToListAsync(ct);

        return types
            .Select(code => new DocumentTypeCategoryLinksDto(
                code,
                links.Where(l => l.Code == code).Select(l => l.CategoryCode).OrderBy(c => c).ToList()))
            .ToList();
    }
}

/// <summary>
/// Replaces the whole link set for one document type.
///
/// <para><b>Whole-set rather than add/remove.</b> The thing an administrator decides is "this document is
/// required for these categories", which is a set; add and remove endpoints would make the same decision take
/// several requests and leave a half-applied state visible in between.</para>
/// </summary>
public sealed class SetDocumentTypeCategoriesHandler(AppDbContext db) : ISetDocumentTypeCategoriesHandler
{
    public async Task<SetDocumentTypeCategoriesResult> HandleAsync(
        SetDocumentTypeCategoriesCommand command, CancellationToken ct)
    {
        var documentType = await db.Set<DocumentType>()
            .FirstOrDefaultAsync(t => t.Code == command.DocumentTypeCode, ct);
        if (documentType is null) return new SetDocumentTypeCategoriesResult.UnknownDocumentType();

        var requested = command.CategoryCodes.Distinct(StringComparer.Ordinal).ToList();

        // Validated against the category table, and this is the check that matters: a link to a category that
        // does not exist is a requirement no supplier can ever match, and it would be invisible until the
        // derivation is switched on - at which point it silently excludes a document from everyone.
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

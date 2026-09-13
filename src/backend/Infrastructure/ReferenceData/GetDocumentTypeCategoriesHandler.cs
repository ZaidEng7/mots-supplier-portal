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

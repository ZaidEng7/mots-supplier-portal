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
                t.Id, t.Code, t.NameAr, t.NameEn, t.IsRequired, t.ExpiryTracked,
                latest is null ? null : UploadDocumentHandler.ToDto(latest));
        })];
    }
}

public sealed class ReviewerListDocumentsHandler(AppDbContext db) : IReviewerListDocumentsHandler
{
    public async Task<IReadOnlyList<DocumentTypeStatusDto>?> HandleAsync(Guid supplierId, CancellationToken ct)
    {
        var exists = await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct);
        if (!exists) return null;
        return await ListSupplierDocumentsHandler.BuildAsync(db, supplierId, ct);
    }
}

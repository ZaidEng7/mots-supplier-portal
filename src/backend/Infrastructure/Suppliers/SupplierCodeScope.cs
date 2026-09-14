// Turning a supplier's public code into an identifier, but only the caller's own.
//
// The ownership test is inside the query rather than a comparison afterwards. A lookup that fetches by code
// and then compares identifiers has already told a timing-sensitive caller that the code exists. One
// statement, one answer.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierCodeScope(AppDbContext db, IScopeContext scope) : ISupplierCodeScope
{
    public async Task<Guid?> ResolveOwnAsync(string supplierCode, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var id = await db.Suppliers
            .Where(s => s.ReferenceCode == supplierCode && s.Id == scope.SupplierId!.Value)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        return id;
    }

    public async Task<bool> DocumentBelongsToSupplierAsync(string supplierCode, string documentCode, CancellationToken ct) =>
        await db.SupplierDocuments
            .AnyAsync(d => d.ReferenceCode == documentCode
                && db.Suppliers.Any(s => s.Id == d.SupplierId && s.ReferenceCode == supplierCode), ct);
}

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// Opaque public reference codes in TYPE-YEAR-SEQ form, e.g. SUP-2026-000001
/// (docs/product/ASSUMPTIONS.md ASM-086). Internal PKs are GUIDv7 and never exposed in URLs.
/// </summary>
public static class ReferenceCodeGenerator
{
    public static async Task<string> NextSupplierCodeAsync(AppDbContext db, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"SUP-{year}-";
        var count = await db.Suppliers.CountAsync(s => s.ReferenceCode.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:D6}";
    }
}

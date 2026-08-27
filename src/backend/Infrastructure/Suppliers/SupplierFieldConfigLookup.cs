using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>Shared lookup for SupplierFieldConfig (FEAT-04.9 compliance re-trigger field list,
/// FEAT-04.2 LegalInfo requiredness) - both are admin-editable per product-owner decision
/// 2026-08-27, replacing what used to be hardcoded call sites/validator rules.</summary>
internal static class SupplierFieldConfigLookup
{
    /// <summary>Missing config rows fall back to <paramref name="defaultValue"/> rather than
    /// throwing, so a config row deleted by mistake degrades to the pre-config hardcoded
    /// behavior instead of breaking the mutation entirely.</summary>
    public static async Task<bool> IsEnabledAsync(AppDbContext db, string category, string fieldCode, bool defaultValue, CancellationToken ct)
    {
        var config = await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == category && c.FieldCode == fieldCode)
            .Select(c => (bool?)c.IsEnabled)
            .FirstOrDefaultAsync(ct);
        return config ?? defaultValue;
    }
}

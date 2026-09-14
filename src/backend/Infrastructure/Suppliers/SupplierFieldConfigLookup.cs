// Reading one of the administrator's switches over supplier fields.
//
// Two families of switch live in that table: which field changes send an approved supplier back for review,
// and which legal-information fields are required. Both became administrator-editable by a recorded product
// decision, replacing call sites and validator rules that had the answer written into them.
//
// A missing row falls back to the caller's default rather than throwing, so a row deleted by mistake
// degrades to the behaviour that existed before the table instead of breaking the mutation entirely.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class SupplierFieldConfigLookup
{
    public static async Task<bool> IsEnabledAsync(AppDbContext db, string category, string fieldCode, bool defaultValue, CancellationToken ct)
    {
        var config = await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == category && c.FieldCode == fieldCode)
            .Select(c => (bool?)c.IsEnabled)
            .FirstOrDefaultAsync(ct);
        return config ?? defaultValue;
    }
}

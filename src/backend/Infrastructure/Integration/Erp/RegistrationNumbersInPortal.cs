// Every registration number the portal already holds, and who holds it - read once, before the import writes.
//
// The preview and the run both need it, so that ErpRegistrationNumbers can decide the same thing for both. It is read
// trimmed, because the unique index it guards against compares trimmed values.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class RegistrationNumbersInPortal
{
    public static async Task<IReadOnlyDictionary<string, RegistrationNumberHolder>> ReadAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.LegalInfo != null && s.LegalInfo.RegistrationNumber != null)
            .Select(s => new { Number = s.LegalInfo!.RegistrationNumber!, s.ExternalId, s.ReferenceCode })
            .ToListAsync(ct);

        return rows
            .Where(r => ErpRegistrationNumbers.Normalised(r.Number) is not null)
            .GroupBy(r => ErpRegistrationNumbers.Normalised(r.Number)!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new RegistrationNumberHolder(group.First().ExternalId, group.First().ReferenceCode),
                StringComparer.Ordinal);
    }
}

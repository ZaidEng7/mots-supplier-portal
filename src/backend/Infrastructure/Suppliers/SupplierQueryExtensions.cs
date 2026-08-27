using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Every child collection SupplierDtoMapper.ToDto reads must be included, or the DTO silently
/// under-reports (empty Addresses/CategoryLinks/etc even when data exists) - this bit us once
/// already for Representatives (missing .Include caused GetMissingProfileFields to falsely show
/// primaryContactPhone as missing). One shared extension point so it can't happen again per-field.
/// </summary>
public static class SupplierQueryExtensions
{
    public static IQueryable<Supplier> IncludeProfile(this IQueryable<Supplier> query) =>
        query
            .Include(s => s.Representatives)
            .Include(s => s.Addresses)
            .Include(s => s.Contacts)
            .Include(s => s.Branches)
            .Include(s => s.BankAccounts)
            .Include(s => s.CategoryLinks);
}

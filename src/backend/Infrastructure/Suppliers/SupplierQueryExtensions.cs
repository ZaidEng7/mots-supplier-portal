// The one place that says which child collections a supplier read has to load.
//
// Every collection the read model reads must be included, or it silently under-reports: empty addresses or
// categories even when the data exists.
//
// That happened once already with representatives. A missing include made the missing-fields list claim the
// primary contact's phone number was absent. One shared extension point so it cannot happen again field by
// field.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

public static class SupplierQueryExtensions
{
    public static IQueryable<Supplier> IncludeProfile(this IQueryable<Supplier> query) =>
        query
            .Include(s => s.Representatives)
            .Include(s => s.Addresses)
            .Include(s => s.Contacts)
            .Include(s => s.Branches)
            .Include(s => s.BankAccounts)
            .Include(s => s.CategoryLinks)
            .AsSplitQuery();
}

// Procurement staff searching catalogue entries across every supplier.
//
//
// THE SUPPLIER'S STATE GATES THE RESULT, NOT ONLY THE ENTRY'S
//
// A supplier suspended after listing an entry must disappear from this search even though the entry row
// itself is untouched by the suspension.
//
// There is no navigation between an entry and its supplier, because both are deliberately separate roots, so
// this is a manual join on the plain identifier rather than an included navigation.
//
//
// THE SEARCH TEXT IS ESCAPED BEFORE IT BECOMES A PATTERN
//
// Interpolated raw, the pattern characters in the search box were syntax rather than characters: a lone
// wildcard matched every row, and a single-character wildcard in the middle of a word matched neighbours.
//
// Not an injection, because the value is still a parameter, but the caller's string stopped meaning what it
// says, which is the same class of surprise.
//
// It is also not a disclosure on THIS endpoint today, because searching with no text already returns every
// active entry, so the widest a wildcard reaches is what the caller could have had anyway. It becomes one
// the day this search is scoped by row or paged by relevance, and that is the day nobody would think to
// look here. Found by a filter-guard sweep and fixed while it is still cheap.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SearchBuyerOfferingsHandler(AppDbContext db) : ISearchBuyerOfferingsHandler
{
    public async Task<IReadOnlyList<BuyerOfferingSearchResultDto>> HandleAsync(string? categoryCode, string? query, CancellationToken ct)
    {
        var offerings = db.Offerings.Where(o => o.IsActive);
        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            offerings = offerings.Where(o => o.CategoryCode == categoryCode);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{LikePattern.Escape(query)}%";
            offerings = offerings.Where(o =>
                EF.Functions.ILike(o.NameEn, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(o.NameAr, pattern, LikePattern.EscapeCharacter));
        }

        var joined = await (
            from o in offerings
            join s in db.Suppliers on o.SupplierId equals s.Id
            where s.LifecycleState == SupplierLifecycleState.Active
            orderby o.NameEn
            select new { Offering = o, Supplier = s }
        ).ToListAsync(ct);

        return joined.Select(x => new BuyerOfferingSearchResultDto(
            x.Offering.Id, x.Supplier.ReferenceCode, x.Supplier.DisplayNameAr, x.Supplier.DisplayNameEn,
            x.Offering.NameAr, x.Offering.NameEn, x.Offering.Description, x.Offering.CategoryCode, x.Offering.UnitOfMeasureCode,
            x.Offering.PriceAmount, x.Offering.CurrencyCode, OfferingDtoMapper.DeserializeAttributes(x.Offering.AttributesJson)))
            .ToList();
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-06.3/FR-OFF-004: procurement staff discovering offerings across all suppliers.
/// FEAT-06.4/FR-OFF-005: gated on Supplier.LifecycleState == Active, not Offering.IsActive alone -
/// a supplier suspended after listing an offering must disappear from buyer search even though the
/// Offering row itself is untouched by suspension. No EF navigation exists between Offering and
/// Supplier (both are deliberately separate aggregate roots, see Offering.cs's doc comment), so
/// this is a manual join on the plain SupplierId FK rather than an owned/included navigation.</summary>
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
            // The caller's text is escaped before it becomes a LIKE PATTERN.
            //
            // Interpolated raw, `%` and `_` in the search box were pattern syntax rather than
            // characters: `?query=%` matched every row and `?query=a_c` matched "abc". Not SQL
            // injection - the value is still a parameter - but the caller's string stopped meaning
            // what it says, which is the same class of surprise.
            //
            // Not a disclosure on THIS endpoint today, because searching with no query already
            // returns every active offering, so the widest a wildcard can reach is what the caller
            // could have had anyway. It becomes one the day this search is row-scoped or paged by
            // relevance, and that is the day nobody would think to look here. Found by EPIC-19's
            // filter-guard check and fixed while it is still cheap.
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

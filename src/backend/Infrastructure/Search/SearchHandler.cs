// Runs one search across tenders, suppliers and offerings.
//
//
// ROW SCOPING IS THE SUBSTANCE OF THIS HANDLER
//
// A search that returns a row the caller could not have opened directly is a disclosure, and a quiet one:
// the title alone tells them a tender exists.
//
// So every kind is scoped separately, using the same condition that kind's own read uses, and a caller
// who holds no permission for a kind gets no rows of that kind rather than a filtered-but-present list.
//
//
// IT IS NOT STEMMED, BY DECISION
//
// The search vectors are built with the database's simple configuration, because no Arabic dictionary
// ships with it.
//
// The consequence for a caller is that this matches whole words and prefixes rather than word forms, and
// the query marks its last word as a prefix so typing continues to narrow rather than suddenly matching
// nothing.

namespace MotsSupplierPortal.Infrastructure.Search;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Search;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using NpgsqlTypes;

public sealed class SearchHandler(AppDbContext db, IScopeContext scope) : ISearchHandler
{
    private const int Cap = 50;

    public async Task<SearchResultsDto> HandleAsync(string query, CancellationToken ct)
    {
        var terms = ToPrefixQuery(query);
        if (terms is null) return new SearchResultsDto(query, [], false);

        var hits = new List<SearchHitDto>();
        hits.AddRange(await SearchRfqsAsync(terms, ct));
        hits.AddRange(await SearchSuppliersAsync(terms, ct));
        hits.AddRange(await SearchOfferingsAsync(terms, ct));

        var ordered = hits.OrderByDescending(h => h.Rank).ThenBy(h => h.TitleEn, StringComparer.Ordinal).ToList();
        return new SearchResultsDto(query, ordered.Take(Cap).ToList(), ordered.Count > Cap);
    }

    private static string? ToPrefixQuery(string query)
    {
        var tokens = Tokenise(query)
            .Take(10) // A ten-word search box query is already unusual; a thousand-token one is a denial of service.
            .ToList();

        if (tokens.Count == 0) return null;

        var last = tokens[^1] + ":*";
        return string.Join(" & ", tokens[..^1].Append(last));
    }

    private static IEnumerable<string> Tokenise(string query)
    {
        var current = new System.Text.StringBuilder();
        foreach (var character in query)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }
            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private async Task<List<SearchHitDto>> SearchRfqsAsync(string terms, CancellationToken ct)
    {
        if (!scope.HasPermission(Permissions.RfqRead)) return [];

        var matching = db.Rfqs.AsNoTracking()
            .Where(r => EF.Property<NpgsqlTsVector>(r, "SearchVector").Matches(EF.Functions.ToTsQuery("simple", terms)));

        if (scope.SupplierId is { } supplierId)
        {
            matching = matching.Where(r =>
                r.State != RfqState.Draft && r.State != RfqState.InternalReview && r.State != RfqState.Approved
                && r.Invitations.Any(i => i.SupplierId == supplierId));
        }
        else if (scope.OrganizationId is { } organizationId)
        {
            matching = matching.Where(r => r.OrganizationId == organizationId);
        }
        else
        {
            return [];
        }

        return await matching
            .OrderByDescending(r => EF.Property<NpgsqlTsVector>(r, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms)))
            .Take(Cap + 1)
            .Select(r => new SearchHitDto(
                "rfq", r.ReferenceCode, r.TitleAr, r.TitleEn, r.State.ToString(),
                EF.Property<NpgsqlTsVector>(r, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms))))
            .ToListAsync(ct);
    }

    private async Task<List<SearchHitDto>> SearchSuppliersAsync(string terms, CancellationToken ct)
    {
        var matching = db.Suppliers.AsNoTracking()
            .Where(s => EF.Property<NpgsqlTsVector>(s, "SearchVector").Matches(EF.Functions.ToTsQuery("simple", terms)));

        if (scope.SupplierId is { } supplierId)
        {
            matching = matching.Where(s => s.Id == supplierId);
        }
        else if (!scope.HasPermission(Permissions.SupplierReview) && !scope.HasPermission(Permissions.SupplierLifecycleManage))
        {
            return [];
        }

        return await matching
            .OrderByDescending(s => EF.Property<NpgsqlTsVector>(s, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms)))
            .Take(Cap + 1)
            .Select(s => new SearchHitDto(
                "supplier", s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, s.LifecycleState.ToString(),
                EF.Property<NpgsqlTsVector>(s, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms))))
            .ToListAsync(ct);
    }

    private async Task<List<SearchHitDto>> SearchOfferingsAsync(string terms, CancellationToken ct)
    {
        var isSupplier = scope.SupplierId is not null;
        if (!isSupplier && !scope.HasPermission(Permissions.OfferingSearch)) return [];

        var matching = db.Offerings.AsNoTracking()
            .Where(o => EF.Property<NpgsqlTsVector>(o, "SearchVector").Matches(EF.Functions.ToTsQuery("simple", terms)));

        var joined = scope.SupplierId is { } supplierId
            ? from o in matching.Where(o => o.SupplierId == supplierId)
              join s in db.Suppliers.AsNoTracking() on o.SupplierId equals s.Id
              select new { Offering = o, Supplier = s }
            : from o in matching.Where(o => o.IsActive)
              join s in db.Suppliers.AsNoTracking().Where(s => s.LifecycleState == SupplierLifecycleState.Active)
                on o.SupplierId equals s.Id
              select new { Offering = o, Supplier = s };

        return await joined
            .OrderByDescending(x => EF.Property<NpgsqlTsVector>(x.Offering, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms)))
            .Take(Cap + 1)
            .Select(x => new SearchHitDto(
                "offering", null, x.Offering.NameAr, x.Offering.NameEn, x.Supplier.ReferenceCode,
                EF.Property<NpgsqlTsVector>(x.Offering, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms))))
            .ToListAsync(ct);
    }
}

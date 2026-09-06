using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Search;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using NpgsqlTypes;

namespace MotsSupplierPortal.Infrastructure.Search;

/// <summary>
/// EPIC-20/SCR-906: one query across RFQs, suppliers and offerings.
///
/// <para><b>Row scoping is the substance of this handler, not a detail of it.</b> A search that returns a
/// row the caller could not have opened directly is a disclosure, and it is a quiet one - the title alone
/// tells them a tender exists. So every kind is scoped separately, with the same predicate the entity's own
/// read uses, and a caller who holds no permission for a kind gets NO rows of that kind rather than a
/// filtered-but-present list.</para>
///
/// <para><b>Not stemmed, by decision.</b> The vectors are built with Postgres's 'simple' configuration
/// because no Arabic dictionary ships with Postgres - see AppDbContext's note. Consequence for a caller:
/// this matches whole words and prefixes, not word forms. The query is built with <c>:*</c> on the last
/// token so typing "cater" finds "catering", which is what makes an unstemmed search usable.</para>
///
/// <para><b>Capped, and it says when it capped.</b> A search box does not need page two as much as it needs
/// to be fast, so the cap is 50 and the response carries <c>truncated</c>. Silently returning the first 50
/// of 400 would make a search that missed the row look like a search that found nothing.</para>
/// </summary>
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

        // Ranked across kinds rather than grouped by kind: grouping puts the answer second whenever the
        // best match is not of the type that happens to be listed first.
        var ordered = hits.OrderByDescending(h => h.Rank).ThenBy(h => h.TitleEn, StringComparer.Ordinal).ToList();
        return new SearchResultsDto(query, ordered.Take(Cap).ToList(), ordered.Count > Cap);
    }

    /// <summary>
    /// Turns what a person typed into a <c>tsquery</c>, and refuses rather than guessing when it is empty.
    ///
    /// <para>Built with <c>&amp;</c> between tokens (every word must appear) and <c>:*</c> on the last one
    /// (the word they are still typing is a prefix). Written by hand rather than with
    /// <c>websearch_to_tsquery</c> because that function does not support prefix matching, and prefix
    /// matching is the difference between a usable search box and one that only works after the last
    /// keystroke.</para>
    ///
    /// <para>Every token is stripped to letters, digits and marks. That is not injection defence - the value
    /// is a parameter - it is meaning defence, the same lesson as the ILIKE escape in
    /// SearchBuyerOfferingsHandler: <c>&amp;</c>, <c>|</c>, <c>!</c> and <c>:</c> are tsquery OPERATORS, so a
    /// caller searching for "R&amp;D" would otherwise be issuing a boolean expression by accident.</para>
    /// </summary>
    private static string? ToPrefixQuery(string query)
    {
        // SPLIT on every non-alphanumeric character rather than stripping them out of each token, and this
        // is the difference between a search box that finds a reference code and one that does not.
        // Stripping was the first version: "RFQ-DEMO-0006" arrived as one token, lost its hyphens, and
        // became the single nonsense word "RFQDEMO0006", which matches nothing - `?q=RFQ-DEMO-0006` returned
        // zero hits against a seeded RFQ of exactly that code. Splitting yields rfq & demo & 0006:*, and
        // Postgres's 'simple' parser splits the STORED code the same way, so the two agree.
        var tokens = Tokenise(query)
            .Take(10) // A ten-word search box query is already unusual; a thousand-token one is a denial of service.
            .ToList();

        if (tokens.Count == 0) return null;

        var last = tokens[^1] + ":*";
        return string.Join(" & ", tokens[..^1].Append(last));
    }

    /// <summary>Words, by the same rule Postgres's 'simple' parser uses: runs of letters and digits, and
    /// everything else is a separator. Empty runs are dropped rather than becoming empty tsquery terms,
    /// which Postgres rejects outright.</summary>
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

    /// <summary>
    /// RFQs, scoped the way the entity's own reads are: a staff caller sees their organisation's
    /// (BRULE-029), and a supplier sees only RFQs they were invited to AND that have reached Published -
    /// the same two conditions SupplierRfqLoader.LoadInvitedAsync applies, because a search that showed a
    /// Draft tender to an invited supplier would leak a tender that is still being written.
    /// </summary>
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
            // Neither a supplier nor a member of an organisation. ministry_viewer lands here, and A-10/D-6
            // is explicit that the Ministry sees aggregates and not individual tenders - so no rows, rather
            // than every organisation's.
            return [];
        }

        // Ordered and capped BEFORE the projection, not after. EF cannot order by a property of a record it
        // is constructing - it answered with "The LINQ expression could not be translated" and a 500 - and
        // sorting after materialising would mean pulling every match into memory to keep fifty.
        return await matching
            .OrderByDescending(r => EF.Property<NpgsqlTsVector>(r, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms)))
            .Take(Cap + 1)
            .Select(r => new SearchHitDto(
                "rfq", r.ReferenceCode, r.TitleAr, r.TitleEn, r.State.ToString(),
                EF.Property<NpgsqlTsVector>(r, "SearchVector").Rank(EF.Functions.ToTsQuery("simple", terms))))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Suppliers, and only for a caller who may review them. A supplier searching finds THEMSELVES and
    /// nobody else: cross-supplier confidentiality (BRULE-029) is not softened by a search box, and
    /// returning their own row is what makes "search for my company" work rather than look broken.
    /// </summary>
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

    /// <summary>
    /// Offerings. Behind offering.search for a buyer - the permission that already gates the catalogue -
    /// and restricted to ACTIVE suppliers' active offerings, which is the same pair of conditions
    /// SearchBuyerOfferingsHandler applies. A supplier sees their own, active or not, because their own
    /// draft catalogue is not a disclosure to themselves.
    /// </summary>
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

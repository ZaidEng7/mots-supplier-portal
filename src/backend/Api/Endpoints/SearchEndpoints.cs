// One search across tenders, suppliers and offerings.
//
// It requires no particular permission, and that is deliberate. There is no single permission meaning
// "may search": what a caller may find depends entirely on which kind of thing they are searching, and
// the handler applies each kind's own rule. A blanket permission here would either lock out somebody who
// can legitimately search one kind, or admit somebody who can search none and hand them an empty page
// that looks like a defect. Signing in is required; what you may find is decided per kind, inside.
//
// A blank search term returns nothing rather than everything. That is the lesson from the query-filter
// guards applied to a search: a missing term falling through to an unfiltered query would hand back the
// caller's entire visible world under the guise of a search result.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Application.Search;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/search", async (string? q, ISearchHandler handler, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q)
                ? Results.Ok(new SearchResultsDto(q ?? string.Empty, [], false))
                : Results.Ok(await handler.HandleAsync(q, ct)))
        .RequireAuthorization()
        .WithTags("Search")
        .WithName("Search");
    }
}

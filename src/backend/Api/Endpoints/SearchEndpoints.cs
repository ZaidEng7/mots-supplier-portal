using MotsSupplierPortal.Application.Search;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// EPIC-20/SCR-906: one search across RFQs, suppliers and offerings.
///
/// <para><b>No RequirePermission, and that is deliberate.</b> There is no single permission that means
/// "may search" - what a caller may find depends entirely on WHICH kind, and the handler applies each
/// entity's own rule. A blanket permission here would either lock out a persona who can legitimately
/// search one kind, or admit one who can search none and return them an empty page that looks like a
/// defect. Authentication is required; authorisation is per kind, inside.</para>
/// </summary>
public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/search", async (string? q, ISearchHandler handler, CancellationToken ct) =>
            // A blank query returns nothing rather than everything. This is the filter-guard lesson from
            // EPIC-19 applied to a search: an absent term that fell through to an unfiltered query would
            // hand back the caller's entire visible world under the guise of a search result.
            string.IsNullOrWhiteSpace(q)
                ? Results.Ok(new SearchResultsDto(q ?? string.Empty, [], false))
                : Results.Ok(await handler.HandleAsync(q, ct)))
        .RequireAuthorization()
        .WithTags("Search")
        .WithName("Search");
    }
}

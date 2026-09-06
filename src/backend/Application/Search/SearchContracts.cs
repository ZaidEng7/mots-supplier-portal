namespace MotsSupplierPortal.Application.Search;

/// <summary>
/// EPIC-20/SCR-906. What a search hit is, regardless of what it is a hit ON.
/// </summary>
/// <param name="Kind">"rfq", "supplier" or "offering". A string rather than an enum because the SPA routes
/// on it and a new searchable entity should not be a breaking wire change (API-ARCHITECTURE's own rule:
/// a new enum value documented as open is additive).</param>
/// <param name="ReferenceCode">The opaque public identifier (§3), and what the SPA builds its link from.
/// Null for an offering, which has no public code of its own - the supplier's code is carried in Context
/// so the row is still reachable.</param>
/// <param name="Rank">Postgres's own relevance for this row against this query. Returned rather than kept
/// private so a caller can see WHY the order is what it is - and so a flat set of equal ranks, which is
/// what an unstemmed single-word query produces, is visible rather than mistaken for arbitrary order.</param>
public sealed record SearchHitDto(
    string Kind,
    string? ReferenceCode,
    string TitleAr,
    string TitleEn,
    string? Context,
    float Rank);

/// <param name="Query">Echoed back, because a search screen that has lost track of what it asked cannot
/// tell "no results" from "you are looking at the previous query".</param>
/// <param name="Hits">Ordered by rank across ALL kinds. Grouping by entity type would put the answer
/// second whenever the best match is not of the type listed first.</param>
public sealed record SearchResultsDto(string Query, IReadOnlyList<SearchHitDto> Hits, bool Truncated);

public interface ISearchHandler
{
    Task<SearchResultsDto> HandleAsync(string query, CancellationToken ct);
}

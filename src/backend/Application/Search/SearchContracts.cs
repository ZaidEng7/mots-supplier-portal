// The vocabulary for one search across tenders, suppliers and offerings.
//
// A hit is the same shape whatever it is a hit on, so one screen renders all three.
//
// Kind is text rather than a fixed set, because the interface routes on it and adding a fourth
// searchable thing should not be a breaking change on the wire.
//
// ReferenceCode is the public identifier the interface builds its link from. It is empty for an
// offering, which has no public code of its own, and the supplier's code travels in the context instead
// so the row is still reachable.
//
// Rank is the database's own relevance for that row against that query. It is returned rather than kept
// private for two reasons: a caller can see why the order is what it is, and a flat set of equal ranks,
// which is what a single-word query produces, is visible rather than mistaken for arbitrary order.
//
// The query is echoed back, because a search screen that has lost track of what it asked cannot tell no
// results from results for the previous question.
//
// Hits are ordered by rank across all three kinds together. Grouping by kind would put the best answer
// second whenever it is not of the kind listed first.

namespace MotsSupplierPortal.Application.Search;

public sealed record SearchHitDto(
    string Kind,
    string? ReferenceCode,
    string TitleAr,
    string TitleEn,
    string? Context,
    float Rank);

public sealed record SearchResultsDto(string Query, IReadOnlyList<SearchHitDto> Hits, bool Truncated);

public interface ISearchHandler
{
    Task<SearchResultsDto> HandleAsync(string query, CancellationToken ct);
}

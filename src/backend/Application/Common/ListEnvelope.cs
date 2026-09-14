// The shape every list response has: the rows, how to get the next page, and what the caller asked for.
//
// One envelope for every collection, so the interface's table components and query code are uniform
// instead of each read having its own shape.
//
//
// PAGING
//
// The mode is either a cursor or a page number. Every read here uses a cursor. Page numbers exist in the
// contract for administrative grids that render a total-count pager, and nothing uses that yet.
//
// The next cursor is absent when there are no more rows.
//
// The previous cursor is always absent, because no read supports paging backwards. It is present in the
// shape because the contract lists it, and emitting a fabricated value would be worse than emitting the
// documented absence.
//
// The total count is absent unless the caller asks for it. It is still reported as absent rather than left
// out entirely, so the shape stays stable for clients.
//
// The page number is absent under cursor paging, where there is no page number to report.
//
//
// HASMORE IS ALWAYS THERE AND THE TOTAL USUALLY IS NOT
//
// Knowing whether more rows exist costs nothing: fetch one more row than the page size, return the page,
// and report whether the extra row was there.
//
// A total needs counting the whole filtered set, which is expensive on a table that only grows and close to
// meaningless under cursor paging, where there is no page twelve of four hundred to render.
//
//
// THE META BLOCK IS ALWAYS EMITTED
//
// Even when nothing was sorted or filtered, in which case its fields are absent. The contract shows it
// populated on a request that carried both and does not say whether it is required when neither applies.
//
// Always emitting it is what the contract's own reasoning asks for, since a key that appears and
// disappears is exactly what forces every reader to be defensive. Recorded as a documented silence.
//
//
// THE SIZE LIMITS
//
// The default page is twenty rows and the ceiling is a hundred.
//
// A missing or nonsensical size falls back to the default rather than failing. The contract documents
// clamping rather than refusing, and failing a list read over a bad query string helps nobody.
//
// WasClamped is separate from the clamp because the clamp alone cannot tell the route whether anything was
// clamped, and the route has to attach a warning header when it was.
//
// Under page numbers there is also a hard cap on how far a caller may skip, which protects the database. It
// is evaluated against the clamped size, because that is what the query will actually use: refusing a
// caller's unclamped request while the server would have run a hundred rows would reject requests the cap
// was never meant to catch.
//
//
// AN EMPTY LIST IS A SUCCESS
//
// No rows is an empty list with a successful status, never a not-found. A caller asking a question with no
// answers has not asked about something that does not exist.

namespace MotsSupplierPortal.Application.Common;

public sealed record PaginationEnvelope(
    string Mode,
    string? NextCursor,
    string? PrevCursor,
    int PageSize,
    int? TotalCount,
    bool HasMore,
    int? Page = null);

public sealed record ListMetaEnvelope(string? Sort, IReadOnlyList<string>? FiltersApplied);

public sealed record ListEnvelope<T>(
    IReadOnlyList<T> Data,
    PaginationEnvelope Pagination,
    ListMetaEnvelope Meta)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public static int ClampPageSize(int? requested) =>
        requested is null or < 1 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);

    public static bool WasClamped(int? requested) => requested is > MaxPageSize;

    public static ListEnvelope<T> Cursor(
        IReadOnlyList<T> data,
        bool hasMore,
        string? nextCursor,
        int pageSize,
        int? totalCount = null,
        string? sort = null,
        IReadOnlyList<string>? filtersApplied = null) =>
        new(data,
            new PaginationEnvelope("cursor", hasMore ? nextCursor : null, PrevCursor: null, pageSize, totalCount, hasMore),
            new ListMetaEnvelope(sort, filtersApplied));

    public const int MaxPageOffset = 10_000;

    public static bool ExceedsPageCap(int page, int? requestedPageSize) =>
        (long)Math.Max(page, 1) * ClampPageSize(requestedPageSize) > MaxPageOffset;

    public static ListEnvelope<T> PageOf(
        IReadOnlyList<T> data,
        int page,
        int pageSize,
        int totalCount,
        string? sort = null,
        IReadOnlyList<string>? filtersApplied = null) =>
        new(data,
            new PaginationEnvelope("page", NextCursor: null, PrevCursor: null, pageSize, totalCount,
                HasMore: (long)page * pageSize < totalCount, Page: page),
            new ListMetaEnvelope(sort, filtersApplied));

    public static ListEnvelope<T> Empty(int pageSize, string? sort = null, IReadOnlyList<string>? filtersApplied = null) =>
        new([],
            new PaginationEnvelope("cursor", NextCursor: null, PrevCursor: null, pageSize, TotalCount: null, HasMore: false),
            new ListMetaEnvelope(sort, filtersApplied));
}

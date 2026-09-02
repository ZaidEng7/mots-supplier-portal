namespace MotsSupplierPortal.Application.Common;

/// <summary>The `pagination` block of the list envelope (API-ARCHITECTURE.md §5.2).</summary>
/// <param name="Mode">"cursor" | "page". Every endpoint here is cursor mode; offset paging exists in
/// the contract for admin grids that render a total-count pager, and no endpoint uses it yet.</param>
/// <param name="NextCursor">Null when no more rows (cursor mode).</param>
/// <param name="PrevCursor">Always null today: no endpoint supports backward paging, and emitting a
/// fabricated value would be worse than emitting the documented null. Present because §5.2 lists it.</param>
/// <param name="TotalCount">Omitted (null) unless the caller asks - §6.1: "totalCount omitted unless
/// ?withCount=true". Serialised as null rather than dropped, so the shape is stable for clients.</param>
/// <param name="Page">Page mode only (§12.3's worked response carries <c>"page": 1</c>); null under
/// cursor mode, where there is no page number to report.</param>
public sealed record PaginationEnvelope(
    string Mode,
    string? NextCursor,
    string? PrevCursor,
    int PageSize,
    int? TotalCount,
    bool HasMore,
    int? Page = null);

/// <summary>
/// The `meta` block of the list envelope (§5.2).
///
/// <para>§5.2 shows `meta` populated on a request that carried both a sort and filters; it does not
/// state whether the block is required when neither is applied. Emitted always, with nulls, rather
/// than omitted - a stable response shape is what §5.2's own rationale asks for ("so table
/// components and query hooks are uniform"), and a key that appears and disappears is the thing that
/// forces defensive readers. Flagged in the batch report as a documented silence.</para>
/// </summary>
public sealed record ListMetaEnvelope(string? Sort, IReadOnlyList<string>? FiltersApplied);

/// <summary>
/// The standard list envelope every collection endpoint returns (API-ARCHITECTURE.md §5.2):
/// <c>{ data, pagination: { mode, nextCursor, prevCursor, pageSize, totalCount, hasMore }, meta:
/// { sort, filtersApplied } }</c>.
///
/// <para><b>Replaces <c>Page&lt;T&gt;</c></b> (`items`/`hasMore`/`nextCursor`), which was a
/// reasonable shape but not the documented one - Block 1.9 of the Epics 7-14 audit. Renamed now,
/// while six handlers use it, rather than after the Epics 15-19 dashboards add more callers.</para>
///
/// <para><b>HasMore is always populated; TotalCount usually is not</b> - carried over from
/// <c>Page&lt;T&gt;</c>, and it matches §6.1. HasMore costs nothing (fetch pageSize + 1, return
/// pageSize, report whether the extra row existed); a total needs a COUNT over the whole filtered
/// set, which is expensive on an append-only table and close to meaningless under keyset paging
/// where there is no "page 12 of 400" to render.</para>
/// </summary>
public sealed record ListEnvelope<T>(
    IReadOnlyList<T> Data,
    PaginationEnvelope Pagination,
    ListMetaEnvelope Meta)
{
    /// <summary>§6.1: "pageSize default <b>20</b>". Compatible with NFR-PERF-006's "default page
    /// ≤ 50 rows" - the stricter of the two governs.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>§6.1: "max <b>100</b> (&gt; 100 → clamped + Warning header)". Was 200 under
    /// <c>Page&lt;T&gt;</c>; lowered to the documented ceiling.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Clamps a caller-supplied page size into the documented range. A missing or nonsensical value
    /// falls back to the default rather than erroring - §6.1 documents clamping, not rejection, and
    /// failing a list read over a bad query string helps nobody.
    /// </summary>
    public static int ClampPageSize(int? requested) =>
        requested is null or < 1 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);

    /// <summary>True when the caller asked for more than the ceiling, so the endpoint can attach the
    /// `Warning` header §6.1 requires. Separate from ClampPageSize because the clamp alone cannot
    /// tell the endpoint whether anything was clamped.</summary>
    public static bool WasClamped(int? requested) => requested is > MaxPageSize;

    /// <summary>A cursor-mode page.</summary>
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

    /// <summary>§6.1, page mode: *"Hard cap `page*pageSize <= 10 000` to protect the DB"*.</summary>
    public const int MaxPageOffset = 10_000;

    /// <summary>
    /// True when <paramref name="page"/> and <paramref name="pageSize"/> would read past §6.1's
    /// hard cap. The endpoint answers 422 *"advising cursor mode"*, which §6.1 states verbatim.
    ///
    /// <para>Evaluated on the CLAMPED page size, because the clamp is what the query will actually
    /// use - refusing on the caller's unclamped 5000 while the server would have run 100 would
    /// reject requests the cap was never meant to catch.</para>
    /// </summary>
    public static bool ExceedsPageCap(int page, int? requestedPageSize) =>
        (long)Math.Max(page, 1) * ClampPageSize(requestedPageSize) > MaxPageOffset;

    /// <summary>
    /// A page-mode page (§6.1: *"Offset paging for finite admin grids. Always returns
    /// `totalCount`"*), shaped as §12.3's worked response shows it: <c>mode</c>, <c>page</c>,
    /// <c>pageSize</c>, <c>totalCount</c>, <c>hasMore</c> - and no cursors, which have no meaning
    /// here and are emitted as null rather than fabricated.
    /// </summary>
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

    /// <summary>An empty cursor-mode page. §5.2: "Empty results return `data: []` with `200`, never
    /// `404`."</summary>
    public static ListEnvelope<T> Empty(int pageSize, string? sort = null, IReadOnlyList<string>? filtersApplied = null) =>
        new([],
            new PaginationEnvelope("cursor", NextCursor: null, PrevCursor: null, pageSize, TotalCount: null, HasMore: false),
            new ListMetaEnvelope(sort, filtersApplied));
}

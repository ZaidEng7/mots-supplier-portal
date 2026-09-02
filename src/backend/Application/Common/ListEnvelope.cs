namespace MotsSupplierPortal.Application.Common;

/// <summary>The `pagination` block of the list envelope (API-ARCHITECTURE.md §5.2).</summary>
/// <param name="Mode">"cursor" | "page". Every endpoint here is cursor mode; offset paging exists in
/// the contract for admin grids that render a total-count pager, and no endpoint uses it yet.</param>
/// <param name="NextCursor">Null when no more rows (cursor mode).</param>
/// <param name="PrevCursor">Always null today: no endpoint supports backward paging, and emitting a
/// fabricated value would be worse than emitting the documented null. Present because §5.2 lists it.</param>
/// <param name="TotalCount">Omitted (null) unless the caller asks - §6.1: "totalCount omitted unless
/// ?withCount=true". Serialised as null rather than dropped, so the shape is stable for clients.</param>
public sealed record PaginationEnvelope(
    string Mode,
    string? NextCursor,
    string? PrevCursor,
    int PageSize,
    int? TotalCount,
    bool HasMore);

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

    /// <summary>An empty cursor-mode page. §5.2: "Empty results return `data: []` with `200`, never
    /// `404`."</summary>
    public static ListEnvelope<T> Empty(int pageSize, string? sort = null, IReadOnlyList<string>? filtersApplied = null) =>
        new([],
            new PaginationEnvelope("cursor", NextCursor: null, PrevCursor: null, pageSize, TotalCount: null, HasMore: false),
            new ListMetaEnvelope(sort, filtersApplied));
}

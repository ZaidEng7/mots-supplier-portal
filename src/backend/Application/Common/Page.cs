namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// One page of a list response (MSP-66, NFR-PERF-006).
///
/// <para><b>HasMore is always populated; Total usually is not.</b> HasMore costs nothing - fetch
/// limit + 1 rows, return limit, report whether the extra row existed - so every paginated endpoint
/// can state truthfully whether the caller has everything. Total needs a COUNT over the whole
/// filtered set, which is cheap on a supplier's addresses and increasingly expensive on the audit
/// log; it is also close to meaningless under keyset paging, where there is no "page 12 of 400" to
/// render. So Total is opt-in per endpoint, only where it is both cheap and useful.</para>
///
/// <para><b>Why a truncation flag at all.</b> A bound without a flag silently truncates: the API
/// returns fewer rows than exist, the client believes it has everything, and nothing anywhere says
/// otherwise. That is the same failure this codebase keeps producing - a response that reports
/// success while quietly being incomplete. Stating HasMore keeps the API honest even while a client
/// ignores it, and makes the eventual client fix a display change rather than a contract
/// change.</para>
/// </summary>
/// <param name="Items">The rows for this page, never more than the requested limit.</param>
/// <param name="HasMore">True when more rows exist beyond this page. Callers that ignore this are
/// choosing to show a partial list; they are not being told a complete one.</param>
/// <param name="NextCursor">Opaque cursor for the next page under keyset paging. Null when there is
/// no next page, or when the endpoint uses offset paging.</param>
/// <param name="Total">Total matching rows, only where counting is cheap and a page count is
/// actually useful to render.</param>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    bool HasMore,
    string? NextCursor = null,
    int? Total = null)
{
    /// <summary>Default page size. NFR-PERF-006 caps a default page at 50 rows.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Hard ceiling on a caller-supplied limit. Without this, `?limit=100000` reinstates
    /// the unbounded query the paging exists to remove - the bound has to be enforced server-side,
    /// not merely offered.</summary>
    public const int MaxLimit = 200;

    /// <summary>Clamps a caller-supplied limit into range. A missing or nonsensical value falls
    /// back to the default rather than erroring: this is a list read, and failing it over a bad
    /// query string helps nobody.</summary>
    public static int ClampLimit(int? requested) =>
        requested is null or < 1 ? DefaultLimit : Math.Min(requested.Value, MaxLimit);
}

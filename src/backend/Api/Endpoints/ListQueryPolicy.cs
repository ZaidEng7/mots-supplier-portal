using System.Text.Json;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// What one list endpoint accepts in its query string, and the endpoint filter that enforces it.
///
/// <para>API-ARCHITECTURE.md §6.2: <i>"Explicit, whitelisted, type-checked query params per endpoint
/// - no generic query-language passthrough"</i> and <i>"Unknown filter key → 422
/// (`type: …/errors/unknown-filter`) rather than silent ignore."</i> §6.3 adds the same rule for
/// sorting: <i>"Only whitelisted sort keys per endpoint; unknown key → 422."</i></para>
///
/// <para><b>Why an endpoint filter rather than per-handler checks.</b> Minimal APIs bind the
/// parameters a handler declares and silently drop everything else, so <c>?stat=Approved</c> (a
/// typo) previously returned an unfiltered list that looked correct - the exact failure §6.2 names.
/// The check has to see the raw query string, which only a filter positioned before model binding
/// can. One implementation, one place to add an endpoint.</para>
/// </summary>
/// <param name="DefaultSort">The sort the endpoint applies when the caller asks for none. Echoed in
/// the envelope's <c>meta.sort</c> by the handler, and documented per endpoint as §6.3 requires.</param>
/// <param name="SortKeys">Sort keys the endpoint can actually order by, without the leading
/// <c>-</c>. Every other key is a 422, never a silently ignored request for an order the caller
/// did not get.</param>
/// <param name="FilterKeys">Filter parameters this endpoint understands, beyond the pagination
/// parameters every list endpoint accepts.</param>
public sealed record ListQueryPolicy(
    string DefaultSort,
    IReadOnlySet<string> SortKeys,
    IReadOnlySet<string> FilterKeys)
{
    /// <summary>
    /// Query parameters every list endpoint accepts, from §6.1 (<c>cursor</c>, <c>pageSize</c>,
    /// <c>withCount</c>) and §6.3 (<c>sort</c>). Deliberately excludes <c>page</c>: no endpoint in
    /// this codebase serves page mode, so <c>?page=2</c> is a caller mistake that would otherwise be
    /// answered with page one of a cursor list - silently wrong in exactly the way §6.2 forbids.
    /// </summary>
    private static readonly HashSet<string> PaginationKeys =
        new(StringComparer.OrdinalIgnoreCase) { "cursor", "pageSize", "withCount", "sort" };

    public static ListQueryPolicy Create(string defaultSort, string[] sortKeys, params string[] filterKeys) =>
        new(defaultSort,
            new HashSet<string>(sortKeys, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(filterKeys, StringComparer.OrdinalIgnoreCase));

    internal bool Accepts(string queryKey) =>
        PaginationKeys.Contains(queryKey) || FilterKeys.Contains(queryKey);

    /// <summary>Splits `?sort=-a,b` into its keys, each stripped of its direction marker.</summary>
    internal static IEnumerable<string> SortKeysIn(string sort) =>
        sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.StartsWith('-') ? k[1..] : k);
}

/// <summary>
/// Rejects query parameters an endpoint does not understand, per §6.2/§6.3.
///
/// <para><b>The `type` slug is the document's, the rest is not.</b> §6.2 names
/// <c>…/errors/unknown-filter</c> and §7.1's catalog gives the base
/// <c>https://api.mots-portal.sy/errors/…</c>, so both are transcribed. §7.1's catalog is an
/// "extract" and carries no row for unknown-filter, so it names no <c>code</c>; §6.3 names no slug
/// at all for a bad sort key, so that case reuses the documented
/// <c>/errors/validation</c> rather than inventing <c>/errors/unknown-sort</c>. Reported as
/// documented silences.</para>
/// </summary>
internal sealed class ListQueryFilter(ListQueryPolicy policy) : IEndpointFilter
{
    private const string TypeBase = "https://api.mots-portal.sy/errors/";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var query = context.HttpContext.Request.Query;

        foreach (var key in query.Keys)
        {
            if (!policy.Accepts(key))
            {
                return Problem($"{TypeBase}unknown-filter", "Unknown filter key.", "UNKNOWN_FILTER",
                    $"'{key}' is not a filter this endpoint accepts.", key);
            }
        }

        if (query.TryGetValue("sort", out var sort) && sort.Count > 0 && !string.IsNullOrWhiteSpace(sort[0]))
        {
            var unknown = ListQueryPolicy.SortKeysIn(sort[0]!)
                .FirstOrDefault(k => !policy.SortKeys.Contains(k));
            if (unknown is not null)
            {
                return Problem($"{TypeBase}validation", "Unknown sort key.", "UNKNOWN_SORT_KEY",
                    $"'{unknown}' is not a sort key this endpoint accepts.", "sort");
            }
        }

        return await next(context);
    }

    /// <summary>
    /// RFC 9457 problem+json. The bilingual <c>errors[]</c> follows §7.2's validation shape - the
    /// only error body this contract specifies in full - so the SPA can render either language
    /// without a round-trip, as §7.2's own rationale requires.
    /// </summary>
    private static IResult Problem(string type, string title, string code, string detail, string field) =>
        Results.Json(new
        {
            type,
            title,
            status = StatusCodes.Status422UnprocessableEntity,
            detail,
            code,
            errors = new[]
            {
                new
                {
                    field,
                    code,
                    messages = new Dictionary<string, string>
                    {
                        ["ar"] = "معامل غير معروف في الطلب.",
                        ["en"] = detail,
                    },
                },
            },
        },
        statusCode: StatusCodes.Status422UnprocessableEntity,
        contentType: "application/problem+json",
        options: JsonSerializerOptions.Web);
}

internal static class ListQueryExtensions
{
    /// <summary>Applies §6.2/§6.3 whitelisting to a list endpoint.</summary>
    public static RouteHandlerBuilder WithListQuery(this RouteHandlerBuilder builder, ListQueryPolicy policy) =>
        builder.AddEndpointFilter(new ListQueryFilter(policy));
}

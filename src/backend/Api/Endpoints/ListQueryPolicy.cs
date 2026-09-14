// What one list route accepts in its query string, and the filter that enforces it.
//
// The contract is explicit: query parameters are explicitly listed, type-checked per route, with no
// general-purpose query language, and an unknown filter key is refused rather than silently ignored. The
// same rule applies to sorting, where only listed sort keys are accepted and anything else is refused.
//
// It is a route filter rather than a check inside each handler because the framework binds the parameters
// a handler declares and silently drops everything else, so a misspelt filter name previously returned an
// unfiltered list that looked correct, which is the exact failure the contract names. The check has to see
// the raw query string, and only a filter positioned before parameter binding can. One implementation, one
// place to add a route.
//
// A policy declares three things. The sort applied when the caller asks for none, which the handler echoes
// back in the response envelope. The sort keys the route can actually order by, written without their
// leading direction marker, where every other key is refused rather than being an order the caller
// silently did not get. And the filter parameters this route understands, beyond the paging parameters
// every list accepts.
//
// The always-accepted set is the cursor, the page size, the count flag and the sort. It deliberately
// leaves out a page number, because no route in this codebase serves numbered pages, so asking for page
// two would otherwise be answered with page one of a cursor-based list, which is silently wrong in exactly
// the way the contract forbids.
//
// The failure type for an unknown filter key is the one the contract names, and the base address comes
// from the catalogue, so both are transcribed. The catalogue calls itself an extract and has no row for
// that case, so it names no code. A bad sort key has no named type at all, so it reuses the documented
// validation one rather than inventing something no document defines. Both reported as documented
// silences.
//
// The refusal body is bilingual and follows the validation shape, which is the only failure body the
// contract specifies in full, so the interface can render either language without asking again.
//
// It is built through the shared failure builder so these guards stop being a special case. They used to
// hand-build their own body, which the reshaping middleware passed through untouched: right media type,
// right type identifier, and missing the path and the two tracing identifiers the contract requires on
// every failure. The bilingual list of errors is preserved as an extra field.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Text.Json.Nodes;
using MotsSupplierPortal.Api.Errors;

public sealed record ListQueryPolicy(
    string DefaultSort,
    IReadOnlySet<string> SortKeys,
    IReadOnlySet<string> FilterKeys)
{
    private static readonly HashSet<string> PaginationKeys =
        new(StringComparer.OrdinalIgnoreCase) { "cursor", "pageSize", "withCount", "sort" };

    public static ListQueryPolicy Create(string defaultSort, string[] sortKeys, params string[] filterKeys) =>
        new(defaultSort,
            new HashSet<string>(sortKeys, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(filterKeys, StringComparer.OrdinalIgnoreCase));

    internal bool Accepts(string queryKey) =>
        PaginationKeys.Contains(queryKey) || FilterKeys.Contains(queryKey);

    internal static IEnumerable<string> SortKeysIn(string sort) =>
        sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.StartsWith('-') ? k[1..] : k);
}

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

    private static IResult Problem(string type, string title, string code, string detail, string field) =>
        new ProblemResult(type, title, code, detail, field);

    private sealed record ProblemResult(string Type, string Title, string Code, string Detail, string Field) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var problem = ProblemResponse.Build(
                httpContext, StatusCodes.Status422UnprocessableEntity, Type, Title, Code, Detail);

            problem["errors"] = new JsonArray(new JsonObject
            {
                ["field"] = Field,
                ["code"] = Code,
                ["messages"] = new JsonObject
                {
                    ["ar"] = "معامل غير معروف في الطلب.",
                    ["en"] = Detail,
                },
            });

            await ProblemResponse.WriteAsync(httpContext, problem);
        }
    }
}

internal static class ListQueryExtensions
{
    public static RouteHandlerBuilder WithListQuery(this RouteHandlerBuilder builder, ListQueryPolicy policy) =>
        builder.AddEndpointFilter(new ListQueryFilter(policy));
}

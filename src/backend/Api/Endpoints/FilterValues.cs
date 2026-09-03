using System.Globalization;
using System.Text.Json.Nodes;
using MotsSupplierPortal.Api.Errors;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// Parsing for filter VALUES, and the 422 an unrecognised one earns.
///
/// <para><b>The document is silent here; the codebase's own precedent is not.</b>
/// API-ARCHITECTURE.md §6.2 rules on an unknown filter KEY - *"Unknown filter key → 422
/// (`type: …/errors/unknown-filter`) rather than silent ignore"* - and says nothing about an
/// unrecognised value. Dropping the value looks harmless until you follow it through: a filter whose
/// only member is dropped becomes an EMPTY filter, and an empty filter returns everything. So
/// <c>?state=Approvd</c> - one transposed letter - answers with the unfiltered set while looking
/// like a working filtered list.</para>
///
/// <para>That is the identical failure shape to the <c>?aggregateTyp=X</c> defect found in Batch
/// 0.2, which returned the entire audit trail for a typo and was undetectable from the caller's
/// side. §6.2's rationale for rejecting the key applies verbatim to the value; only the letter of
/// the clause does not reach it.</para>
///
/// <para><b>The slug is transcribed, not invented.</b> §7.1's catalog has no row for a bad filter
/// value, so this reuses the documented <c>/errors/validation</c> - the same choice made for
/// <c>UNKNOWN_SORT_KEY</c> in <see cref="ListQueryFilter"/> - rather than minting
/// <c>/errors/invalid-filter-value</c>, which no document defines.</para>
/// </summary>
internal static class FilterValues
{
    private const string ValidationType = "https://api.mots-portal.sy/errors/validation";

    /// <summary>
    /// Parses §6.2's multi-value OR form (<c>?state=UnderReview,Rejected</c>) into enum members.
    /// Returns false and reports the first unrecognised token, which the caller turns into a 422.
    /// A null or blank filter is "no filter" and parses successfully to null.
    /// </summary>
    public static bool TryParseEnumCsv<TEnum>(string? raw, out List<TEnum>? values, out string? invalidToken)
        where TEnum : struct, Enum
    {
        values = null;
        invalidToken = null;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        var parsed = new List<TEnum>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Case-SENSITIVE on purpose: these are enum member names as the API emits them, and
            // accepting "underreview" here would make the filter's accepted vocabulary wider than
            // the vocabulary of the responses it filters.
            if (!Enum.TryParse<TEnum>(token, ignoreCase: false, out var value) || !Enum.IsDefined(value))
            {
                invalidToken = token;
                return false;
            }
            parsed.Add(value);
        }

        values = parsed;
        return true;
    }

    /// <summary>
    /// Validates a filter whose accepted values are a NAMED SUBSET rather than a whole enum - the
    /// review queue accepts three of SupplierOnboardingState's nine members, so
    /// <c>Enum.TryParse</c> would accept "Approved" and then silently fall through to the unfiltered
    /// default, which is the very failure being closed.
    /// </summary>
    public static bool IsAllowed(string? raw, IReadOnlySet<string> allowed, out string? invalidToken)
    {
        invalidToken = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!allowed.Contains(token))
            {
                invalidToken = token;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// For a filter whose accepted values are a few LITERALS or an identifier - the review queue's
    /// <c>?assignedTo=</c> takes "me", "unassigned", or a specific reviewer's id.
    ///
    /// <para>A malformed id is an invalid VALUE, not an absent filter. Before this, anything that
    /// was neither literal nor a parseable Guid fell out of the handler's if/else chain having
    /// applied no predicate at all, so <c>?assignedTo=grbage</c> returned the whole queue - the same
    /// silent widening as an unrecognised enum member, on a screen procurement staff use daily.</para>
    /// </summary>
    public static bool IsAllowedLiteralOrGuid(string? raw, IReadOnlySet<string> literals, out string? invalidToken)
    {
        invalidToken = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (literals.Contains(raw) || Guid.TryParse(raw, out _)) return true;

        invalidToken = raw;
        return false;
    }

    /// <summary>
    /// RFC 9457 problem+json, bilingual per §7.2's validation shape, built through
    /// <see cref="ProblemResponse"/> so it carries §7's full base shape - instance, traceId and
    /// correlationId included - rather than only the members this guard happens to set.
    /// </summary>
    /// <summary>
    /// Parses a date-bound query parameter, refusing what it cannot read.
    ///
    /// <para><b>The bug this exists to stop.</b> Binding <c>?from</c> straight to
    /// <c>DateTimeOffset?</c> means a malformed value binds to NULL - and a null bound is not a
    /// rejected filter, it is an ABSENT one. <c>?from=nonsense</c> therefore returned rows older
    /// than the caller asked for, silently. Same widening class as the unknown-filter-value bugs
    /// §12-A fixed, reached by a different route: model binding rather than a parse chain.</para>
    ///
    /// <para>EPIC-19 is what makes it matter. An audit export whose date range quietly widened is
    /// the artefact that gets attached to a dispute, and nothing about the file looks wrong.</para>
    ///
    /// <para>Round-trip formats only (ISO-8601). A date parsed under the server's current culture
    /// would make the same query mean different ranges on different hosts, which is the §12.5 bug
    /// that reached a 500 before it was pinned.</para>
    /// </summary>
    public static bool TryParseDateBound(string? raw, out DateTimeOffset? value, out string? invalidToken)
    {
        value = null;
        invalidToken = null;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            value = parsed;
            return true;
        }

        invalidToken = raw;
        return false;
    }

    public static IResult InvalidFilterValue(string field, string invalidToken) =>
        new InvalidFilterValueResult(field, invalidToken);

    private sealed record InvalidFilterValueResult(string Field, string InvalidToken) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var detail = $"'{InvalidToken}' is not a value the '{Field}' filter accepts.";
            var problem = ProblemResponse.Build(
                httpContext, StatusCodes.Status422UnprocessableEntity, ValidationType,
                "Unknown filter value.", "INVALID_FILTER_VALUE", detail);

            problem["errors"] = new JsonArray(new JsonObject
            {
                ["field"] = Field,
                ["code"] = "INVALID_FILTER_VALUE",
                ["messages"] = new JsonObject
                {
                    ["ar"] = "قيمة غير معروفة في عامل التصفية.",
                    ["en"] = detail,
                },
            });

            await ProblemResponse.WriteAsync(httpContext, problem);
        }
    }
}

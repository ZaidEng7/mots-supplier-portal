using System.Text.Json;

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

    /// <summary>RFC 9457 problem+json, bilingual per §7.2's validation shape.</summary>
    public static IResult InvalidFilterValue(string field, string invalidToken) =>
        Results.Json(new
        {
            type = ValidationType,
            title = "Unknown filter value.",
            status = StatusCodes.Status422UnprocessableEntity,
            code = "INVALID_FILTER_VALUE",
            detail = $"'{invalidToken}' is not a value the '{field}' filter accepts.",
            errors = new[]
            {
                new
                {
                    field,
                    code = "INVALID_FILTER_VALUE",
                    messages = new Dictionary<string, string>
                    {
                        ["ar"] = "قيمة غير معروفة في عامل التصفية.",
                        ["en"] = $"'{invalidToken}' is not a value the '{field}' filter accepts.",
                    },
                },
            },
        },
        statusCode: StatusCodes.Status422UnprocessableEntity,
        contentType: "application/problem+json",
        options: JsonSerializerOptions.Web);
}

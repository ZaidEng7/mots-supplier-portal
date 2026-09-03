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
    /// Parses a date-bound query parameter into the right error, and pins the format.
    ///
    /// <para><b>Corrected rationale.</b> An earlier version of this comment claimed that binding
    /// <c>?from</c> to <c>DateTimeOffset?</c> made a malformed value bind to NULL, so
    /// <c>?from=nonsense</c> silently widened the range. <b>That was wrong and was never observed.</b>
    /// ASP.NET Core minimal APIs do not bind an unparseable value to null - they throw
    /// <c>BadHttpRequestException</c>, which this API's middleware shapes into a 400. The request was
    /// always refused. Verified by probing the running API on a parameter that still binds this way
    /// (<c>?pageSize=abc</c>), which answers 400 <c>MALFORMED_JSON</c>, not 200 with a default.</para>
    ///
    /// <para><b>What is actually wrong with that 400.</b> It is the wrong error for the situation and
    /// it says nothing useful. The request is syntactically fine and one filter VALUE is
    /// unprocessable, which is 422 and not 400; the code is <c>MALFORMED_JSON</c> on a GET that
    /// carries no JSON at all; and the body names no field and carries no bilingual <c>errors[]</c>,
    /// so the SPA cannot mark the input the user got wrong and has nothing to render in Arabic. Every
    /// other filter guard in this file already answers 422/<c>INVALID_FILTER_VALUE</c> with both. This
    /// is a contract fix, not a data-exposure fix, and it is worth having on those terms alone.</para>
    ///
    /// <para>Round-trip formats only (ISO-8601). A date parsed under the server's current culture
    /// would make the same query mean different ranges on different hosts, which is the §12.5 bug
    /// that reached a 500 before it was pinned. This part was and remains real.</para>
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

    /// <summary>
    /// Parses an identifier-valued filter, refusing what it cannot read.
    ///
    /// <para>Same mechanism and same correction as <see cref="TryParseDateBound"/>: bound to
    /// <c>Guid?</c>, <c>?actorUserId=not-a-guid</c> was already REFUSED by model binding - it never
    /// widened to every actor's rows. What it earned was a 400 naming no field, which tells a caller
    /// that mistyped one id nothing about which one. This makes it the 422 the rest of the filter
    /// vocabulary uses.</para>
    ///
    /// <para>Deliberately <c>Guid.TryParse</c> rather than <c>TryParseExact("D")</c>: the braced and
    /// hyphenless forms are the same identifier, and rejecting them would refuse a value that is not
    /// actually ambiguous. Only what cannot name an id at all is refused.</para>
    /// </summary>
    public static bool TryParseGuidFilter(string? raw, out Guid? value, out string? invalidToken)
    {
        value = null;
        invalidToken = null;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (Guid.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        invalidToken = raw;
        return false;
    }

    /// <summary>
    /// Parses a boolean query parameter, refusing what it cannot read.
    ///
    /// <para><b>Corrected: this does not fail open.</b> The claim that a malformed <c>bool?</c> binds
    /// to null - so <c>?unreadOnly=maybe</c> returns every notification, read ones included - was
    /// wrong and was never observed. Binding refuses it with a 400, exactly as it does for a date or
    /// a Guid. The filter has never failed open.</para>
    ///
    /// <para>It is fixed here for the same contract reason as the others: 400
    /// <c>MALFORMED_JSON</c> is the wrong code for an unprocessable filter value on a GET with no
    /// body, and it names no field. <c>?withCount</c> takes the same treatment because it binds the
    /// same way; an exception for the parameter that matters least would just be a second
    /// vocabulary.</para>
    ///
    /// <para>Accepted vocabulary is <c>bool.TryParse</c>'s: "true"/"false", case-insensitive. NOT
    /// "1"/"0"/"yes" - widening what the filter accepts is a separate decision from refusing what it
    /// cannot read, and this is only the second.</para>
    /// </summary>
    public static bool TryParseBoolFilter(string? raw, out bool value, out string? invalidToken)
    {
        value = false;
        invalidToken = null;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        invalidToken = raw;
        return false;
    }

    /// <summary>
    /// Reads a boolean filter that has already been validated by <see cref="TryParseBoolFilter"/>.
    /// Separate from the parse so a call site cannot accidentally treat "not a boolean" as false -
    /// the thing this whole guard exists to stop.
    /// </summary>
    public static bool BoolOrFalse(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var parsed) && parsed;

    /// <summary>
    /// RFC 9457 problem+json, bilingual per §7.2's validation shape, built through
    /// <see cref="ProblemResponse"/> so it carries §7's full base shape - instance, traceId and
    /// correlationId included - rather than only the members this guard happens to set.
    /// </summary>
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

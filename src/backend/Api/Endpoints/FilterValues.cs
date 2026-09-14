// Parsing filter values, and the refusal an unrecognised one earns.
//
//
// WHY AN UNRECOGNISED VALUE IS REFUSED AT ALL
//
// The contract rules on an unknown filter key and says nothing about an unrecognised filter value. Dropping
// the value looks harmless until you follow it through: a filter whose only value is dropped becomes an
// empty filter, and an empty filter returns everything. So one transposed letter in a state name answers
// with the unfiltered set while looking like a working filtered list.
//
// That is the identical failure to a misspelt record-type filter found earlier, which returned the entire
// audit trail for a typo and was undetectable from the caller's side. The contract's reasoning for
// refusing the key applies word for word to the value; only the letter of the clause does not reach it.
//
// The failure type is transcribed rather than invented. The catalogue has no row for a bad filter value, so
// this reuses the documented validation one, the same choice made for a bad sort key, rather than minting
// one no document defines.
//
//
// THE PARSERS
//
// TryParseEnumCsv handles the comma-separated form, turning several values into a set the handler treats as
// alternatives. It reports the first unrecognised value, which the caller turns into a refusal. A blank
// filter is no filter and parses successfully to nothing.
//
// It is deliberately case-sensitive. These are the names as the API emits them, and accepting a
// lower-cased version would make what the filter accepts wider than the vocabulary of the responses it
// filters.
//
// TryParseAllowedEnumCsv handles a filter whose accepted values are a named subset rather than a whole set
// of names. The review queue accepts three of nine possible states, so a plain parse would accept a fourth
// and then silently fall through to the unfiltered default, which is the very failure being closed.
//
// IsAllowedLiteralOrGuid handles a filter that takes one of a few words or an identifier, such as the
// review queue's assignee, which takes "me", "unassigned", or a particular reviewer.
//
// A malformed identifier there is an invalid value rather than an absent filter. Before this, anything that
// was neither a known word nor a readable identifier fell out of the handler's branches having applied no
// filter at all, so a typo returned the whole queue: the same silent widening as an unrecognised state, on
// a screen procurement staff use daily.
//
// TryParseBoolFilter and BoolOrFalse are two halves on purpose, so a call site cannot accidentally treat
// "not a true-or-false value" as false, which is the thing this whole guard exists to stop. The accepted
// words are true and false in any capitalisation, and not one, zero or yes: widening what a filter accepts
// is a separate decision from refusing what it cannot read, and this is only the second.
//
//
// THE DATE BOUNDS, AND A CORRECTED EXPLANATION
//
// An earlier version of this explanation claimed that binding a date parameter made a malformed value
// arrive as nothing, so a nonsense value silently widened the range. That was wrong and was never
// observed. The framework does not bind an unreadable value to nothing; it throws, and this API's
// middleware turns that into a refusal. The request was always refused. Verified by probing the running
// API on a parameter that still binds that way, which answers a refusal rather than a success with a
// default.
//
// What is actually wrong with that refusal is that it is the wrong one and it says nothing useful. The
// request is syntactically fine and one filter value is unprocessable, which is a different status. The
// code says the JSON was malformed, on a request that carries no JSON at all. And the body names no field
// and carries no bilingual text, so the interface cannot mark the input the user got wrong and has nothing
// to render in Arabic. Every other filter guard in this file already answers with both. This is a contract
// fix rather than a data-exposure fix, and it is worth having on those terms alone.
//
// Only round-trip date formats are accepted. A date parsed under the server's own locale would make the
// same query mean different ranges on different hosts, which is a defect that reached a server error once
// before it was pinned. That part was and remains real.
//
// The count flag gets the same treatment because it binds the same way, and an exception for the parameter
// that matters least would only be a second vocabulary.
//
// The refusal is built through the shared failure builder, so it carries the full base shape including the
// path and both tracing identifiers rather than only the fields this guard happens to set.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Globalization;
using System.Text.Json.Nodes;
using MotsSupplierPortal.Api.Errors;

internal static class FilterValues
{
    private const string ValidationType = "https://api.mots-portal.sy/errors/validation";

    public static bool TryParseEnumCsv<TEnum>(string? raw, out List<TEnum>? values, out string? invalidToken)
        where TEnum : struct, Enum
    {
        values = null;
        invalidToken = null;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        var parsed = new List<TEnum>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
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

    public static bool IsAllowedLiteralOrGuid(string? raw, IReadOnlySet<string> literals, out string? invalidToken)
    {
        invalidToken = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (literals.Contains(raw) || Guid.TryParse(raw, out _)) return true;

        invalidToken = raw;
        return false;
    }

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

    public static bool BoolOrFalse(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var parsed) && parsed;

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

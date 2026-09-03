using System.Text.Json.Nodes;
using FluentValidation.Results;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// Builds §7.2's field-scoped bilingual validation body.
///
/// <para>Replaces <c>Results.ValidationProblem(validation.ToDictionary())</c>, which produced a 400
/// carrying FluentValidation's own English sentences keyed by PascalCase property name. §7.2 asks for
/// three things that shape does not have: the status is <b>422</b>, the payload is an
/// <c>errors[]</c> array rather than a dictionary, and each entry carries <b>both</b> languages so the
/// SPA renders in the active locale without a round-trip.</para>
///
/// <para><b>Field paths are camelCased per segment</b> - <c>Items[0].UnitPrice</c> becomes
/// <c>items[0].unitPrice</c> - because §7.2 says the paths exist so React Hook Form can map an error
/// straight onto an input, and the SPA's forms are registered against the JSON names.</para>
/// </summary>
public static class ValidationProblems
{
    /// <summary>
    /// Fields whose value must never be echoed. §7.2: "<c>attemptedValue</c> is included only for
    /// non-sensitive fields (never for passwords/tokens)". Matched on the property name's last
    /// segment, case-insensitively, so <c>Reset.NewPassword</c> is caught as well as <c>Password</c>.
    /// </summary>
    private static readonly string[] SensitiveFields =
        ["password", "newpassword", "currentpassword", "token", "code", "secret", "otp"];

    /// <summary>
    /// FluentValidation's default error codes, mapped to the rule names the catalogue is keyed by.
    /// Anything not listed has no catalogue entry by construction and is caught by the coverage test.
    /// </summary>
    private static readonly Dictionary<string, string> RuleNames = new(StringComparer.Ordinal)
    {
        ["NotEmptyValidator"] = "NotEmpty",
        ["NotNullValidator"] = "NotNull",
        ["MaximumLengthValidator"] = "MaximumLength",
        ["MinimumLengthValidator"] = "MinimumLength",
        ["ExactLengthValidator"] = "Length",
        ["LengthValidator"] = "Length",
        ["EmailValidator"] = "EmailAddress",
        ["AspNetCoreCompatibleEmailValidator"] = "EmailAddress",
        ["GreaterThanValidator"] = "GreaterThan",
        ["GreaterThanOrEqualValidator"] = "GreaterThanOrEqualTo",
        ["LessThanValidator"] = "LessThan",
        ["LessThanOrEqualValidator"] = "LessThanOrEqualTo",
        ["InclusiveBetweenValidator"] = "InclusiveBetween",
        ["RegularExpressionValidator"] = "Matches",
        ["PredicateValidator"] = "Must",
        ["AsyncPredicateValidator"] = "MustAsync",
    };

    public static string? RuleNameFor(string errorCode) =>
        RuleNames.TryGetValue(errorCode, out var rule) ? rule : null;

    public static IResult From(ValidationResult validation) => new ValidationProblemResult(validation);

    internal static JsonObject Build(HttpContext context, ValidationResult validation)
    {
        var problem = ProblemResponse.Build(
            context,
            StatusCodes.Status422UnprocessableEntity,
            ProblemTypes.Validation,
            "One or more validation errors occurred.",
            code: "VALIDATION_FAILED",
            detail: null);

        var errors = new JsonArray();
        foreach (var failure in validation.Errors)
        {
            var rule = RuleNameFor(failure.ErrorCode ?? string.Empty);
            // §12.5's PATCH re-paths a failure to where it sits in the merge patch body
            // ("items[0].UnitPrice"), because §7.2's paths exist for the editor to map onto an
            // input. The catalogue is keyed by the rule's own property, so the lookup falls back to
            // the last segment - otherwise every patched field would answer in English.
            var entry = rule is null
                ? null
                : ValidationCatalogue.Find(failure.PropertyName, rule)
                  ?? ValidationCatalogue.Find(LastSegment(failure.PropertyName), rule);

            var error = new JsonObject
            {
                ["field"] = CamelCasePath(failure.PropertyName ?? string.Empty),
                ["code"] = entry?.Code ?? "VALIDATION_FAILED",
                ["messages"] = new JsonObject
                {
                    // A missing entry cannot reach here in a built tree - the coverage test fails
                    // first - but if it ever did, FluentValidation's English is a better answer than
                    // an empty string, and it is identical in both slots so the gap is visible.
                    ["ar"] = entry?.Ar ?? failure.ErrorMessage,
                    ["en"] = entry?.En ?? failure.ErrorMessage,
                },
            };

            if (!IsSensitive(failure.PropertyName) && failure.AttemptedValue is not null)
            {
                error["attemptedValue"] = JsonValue.Create(failure.AttemptedValue.ToString());
            }

            errors.Add(error);
        }

        problem["errors"] = errors;
        return problem;
    }

    private static string LastSegment(string? propertyName) =>
        (propertyName ?? string.Empty).Split('.')[^1];

    private static bool IsSensitive(string? propertyName)
    {
        var last = (propertyName ?? string.Empty).Split('.')[^1];
        return SensitiveFields.Contains(last, StringComparer.OrdinalIgnoreCase);
    }

    private static string CamelCasePath(string? propertyName) =>
        string.Join('.', (propertyName ?? string.Empty).Split('.').Select(CamelCaseSegment));

    private static string CamelCaseSegment(string segment)
    {
        // "Items[0]" keeps its index; only the name part is lowered.
        var bracket = segment.IndexOf('[', StringComparison.Ordinal);
        var name = bracket < 0 ? segment : segment[..bracket];
        var suffix = bracket < 0 ? string.Empty : segment[bracket..];

        if (name.Length == 0 || !char.IsUpper(name[0])) return segment;
        return char.ToLowerInvariant(name[0]) + name[1..] + suffix;
    }


    /// <summary>
    /// §12.5's PATCH could not be read as a JSON object. Not a validation failure - there are no
    /// fields to name - so it is 400 MALFORMED_JSON rather than 422.
    /// </summary>
    public static IResult MalformedMergePatch(HttpContext context) =>
        new MalformedResult(ProblemResponse.Build(context, StatusCodes.Status400BadRequest,
            ProblemTypes.MalformedRequest, "The request body could not be read.", "MALFORMED_JSON",
            "A JSON Merge Patch body must be a JSON object."));

    private sealed record MalformedResult(JsonObject Body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => ProblemResponse.WriteAsync(httpContext, Body);
    }

    private sealed record ValidationProblemResult(ValidationResult Validation) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) =>
            ProblemResponse.WriteAsync(httpContext, Build(httpContext, Validation));
    }
}

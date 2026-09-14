// Builds the body of a validation failure: which fields were wrong, why, in both languages.
//
// It replaces the framework's own validation response, which produced a 400 carrying English
// sentences keyed by the code's own property names. The written contract asks for three things that
// shape does not have. The status is 422, not 400. The payload is a list of errors rather than a
// dictionary. And each entry carries both languages, so the interface renders in the reader's
// language without asking the server again.
//
// Field paths are converted to the naming the wire uses, so a failure on the first item's unit price
// arrives named the way the form field is named. The paths exist so the interface can put an error
// straight onto the input the reader is looking at, and the forms are registered against those names.
//
// SensitiveFields is the list of fields whose value is never echoed back. The contract says the
// attempted value may be included only for fields that are not sensitive, never for passwords or
// tokens. Matching is on the last part of the field path, case-insensitively, so a nested new-password
// field is caught as well as a top-level one.
//
// RuleNames maps the validation library's own internal rule names onto the names the message
// catalogue is keyed by. Anything not listed has no catalogue entry by construction, and the coverage
// test is what catches that.
//
// The message lookup falls back to the last part of the field path. A partial update re-paths a
// failure to where it sits in the patch body, while the catalogue is keyed by the rule's own property,
// so without the fallback every patched field would answer in English.
//
// If a catalogue entry were ever missing, the library's English sentence is used in both language
// slots. That cannot happen in a built product, because the coverage test fails first, but putting the
// same text in both slots makes the gap visible rather than silent.
//
// MalformedMergePatch is a different failure: the body could not be read as an object at all. There
// are no fields to name, so it is a 400 about the request rather than a 422 about its contents.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json.Nodes;
using FluentValidation.Results;

public static class ValidationProblems
{
    private static readonly string[] SensitiveFields =
        ["password", "newpassword", "currentpassword", "token", "code", "secret", "otp"];

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
        var bracket = segment.IndexOf('[', StringComparison.Ordinal);
        var name = bracket < 0 ? segment : segment[..bracket];
        var suffix = bracket < 0 ? string.Empty : segment[bracket..];

        if (name.Length == 0 || !char.IsUpper(name[0])) return segment;
        return char.ToLowerInvariant(name[0]) + name[1..] + suffix;
    }

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

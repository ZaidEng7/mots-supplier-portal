// An email rewording refused because of its placeholders, with the offending placeholders named on the
// response.
//
// An email template's wording contains placeholders the system fills in, such as a verification link.
// Two things can go wrong: the new wording drops a placeholder the email cannot be sent without, or it
// uses one the system has no value for.
//
// This is its own result type rather than a plain object because the first version returned one and the
// placeholders vanished. The middleware reshapes every failure into the standard problem format, so a
// plain object's shape does not survive. A test reading the field off the response found it missing,
// which is the middleware doing exactly what it is for.
//
// Built the same way the illegal-transition refusal is: produce the standard problem document, then
// attach the extra field the caller needs. "A placeholder is missing" is not actionable. "The Arabic
// body no longer contains the verification link" is, and that difference is the whole value of this
// refusal.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json.Nodes;

internal sealed record TokenContractResult(string Code, string Title, IReadOnlyList<string> Tokens) : IResult
{
    public static TokenContractResult MissingRequired(IReadOnlyList<string> tokens) => new(
        "MISSING_REQUIRED_TOKENS",
        "The wording drops a token this email cannot be sent without.",
        tokens);

    public static TokenContractResult Unknown(IReadOnlyList<string> tokens) => new(
        "UNKNOWN_TOKENS",
        "The wording uses a token the payload cannot fill.",
        tokens);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = ProblemResponse.Build(
            httpContext, StatusCodes.Status422UnprocessableEntity, ProblemTypes.Validation,
            Title, code: Code,
            detail: $"Tokens: {string.Join(", ", Tokens)}");

        problem["tokens"] = new JsonArray([.. Tokens.Select(token => JsonValue.Create(token))]);

        return ProblemResponse.WriteAsync(httpContext, problem);
    }
}

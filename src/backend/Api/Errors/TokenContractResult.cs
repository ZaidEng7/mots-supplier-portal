using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// T-076: an email rewording refused because of its tokens, with the tokens NAMED on the response.
///
/// <para><b>Why this type exists rather than an anonymous body.</b> The first version returned
/// <c>Results.UnprocessableEntity(new { error, tokens })</c> and the tokens vanished: §7's
/// ProblemDetailsMiddleware reshapes every non-2xx into problem+json, so the anonymous object's shape does
/// not survive. Found by a test reading <c>error</c> off the response and getting a KeyNotFoundException -
/// which is the middleware doing exactly what it is for.</para>
///
/// <para>Modelled on IllegalTransitionResult: build the problem document, then attach the extension member
/// the caller needs. "A token is missing" is not actionable; "the Arabic body no longer contains
/// {verifyUrl}" is, and that difference is the whole value of this refusal.</para>
/// </summary>
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

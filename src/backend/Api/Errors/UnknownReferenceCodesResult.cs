// A write refused because it named reference codes that do not exist, with the offending codes listed
// on the response.
//
// It is a result type of its own for the same reason the token refusal is. The middleware reshapes
// every failure into the standard problem format, so a plain object carrying the codes would not
// survive the trip. And telling an administrator only that "one of these is not a category" sends them
// to compare two lists by eye.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json.Nodes;

internal sealed record UnknownReferenceCodesResult(string Field, IReadOnlyList<string> Codes) : IResult
{
    public static UnknownReferenceCodesResult For(string field, IReadOnlyList<string> codes) => new(field, codes);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = ProblemResponse.Build(
            httpContext, StatusCodes.Status422UnprocessableEntity, ProblemTypes.Validation,
            "One or more reference codes do not exist.",
            code: "UNKNOWN_REFERENCE_CODES",
            detail: $"{Field}: {string.Join(", ", Codes)}");

        problem["field"] = Field;
        problem["codes"] = new JsonArray([.. Codes.Select(code => JsonValue.Create(code))]);

        return ProblemResponse.WriteAsync(httpContext, problem);
    }
}

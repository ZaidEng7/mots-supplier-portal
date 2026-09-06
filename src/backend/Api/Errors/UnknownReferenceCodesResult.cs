using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// A write refused because it named reference codes that do not exist, with the codes on the response.
///
/// <para>Same shape and same reason as <see cref="TokenContractResult"/>: §7's middleware reshapes every
/// non-2xx into problem+json, so an anonymous <c>{ error, codes }</c> body does not survive - and "one of
/// these is not a category" sends an administrator to compare two lists by eye.</para>
/// </summary>
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

// Builds the standard failure body every non-success response carries, in one place.
//
// The shape is the type of failure, a title, the status, a human-readable detail, the path, a
// machine-readable code, and two identifiers for tracing. One builder, because the contract requires
// it of every response except a not-modified.
//
// The trace identifier is the real one from the tracing system rather than a fresh number per
// response. The contract says it must always be present, including on a server error, and that it
// exists so somebody can jump from a response straight to the logs. An identifier generated per
// response would satisfy the shape and none of the purpose.
//
// The correlation identifier comes from the same source the audit trail uses, so a failure response
// and the audit rows written while handling that request carry the same one. Two independently
// generated identifiers would look right in a response body and join to nothing.
//
// The detail and the code are optional in shape but not decoration. Clients are meant to switch on
// the type and the code, so a response that omits the code forces them onto the human-readable text,
// which is exactly what the contract forbids. Both are emitted whenever there is one.
//
// ServerError is built from nothing but the request. No exception message, no stack trace, no
// database text. The contract is explicit about that, and the reason it is explicit is that a
// database exception routinely carries a table name, a column name and sometimes the offending value.
// The title is a constant rather than anything derived from the exception, so there is no path by
// which internal text can reach the body.

namespace MotsSupplierPortal.Api.Errors;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MotsSupplierPortal.Application.Common;

public static class ProblemResponse
{
    private const string ContentType = "application/problem+json";

    private const string GenericServerErrorTitle = "An unexpected error occurred.";

    public static JsonObject Build(
        HttpContext context,
        int status,
        string type,
        string title,
        string? code,
        string? detail)
    {
        var problem = new JsonObject
        {
            ["type"] = type,
            ["title"] = title,
            ["status"] = status,
            ["instance"] = context.Request.Path.Value,
            ["traceId"] = Activity.Current?.TraceId.ToString(),
            ["correlationId"] = CorrelationIdOf(context),
        };

        if (detail is not null) problem["detail"] = detail;
        if (code is not null) problem["code"] = code;

        return problem;
    }

    public static JsonObject ServerError(HttpContext context) =>
        Build(context, StatusCodes.Status500InternalServerError, ProblemTypes.Internal,
            GenericServerErrorTitle, code: "INTERNAL_ERROR", detail: null);

    public static async Task WriteAsync(HttpContext context, JsonObject problem)
    {
        context.Response.StatusCode = problem["status"]!.GetValue<int>();
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(problem.ToJsonString(JsonSerializerOptions.Web), context.RequestAborted);
    }

    private static string? CorrelationIdOf(HttpContext context) =>
        context.RequestServices.GetService<IAuditContext>()?.CorrelationId.ToString();
}

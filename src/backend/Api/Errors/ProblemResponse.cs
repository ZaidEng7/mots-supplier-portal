using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// Builds API-ARCHITECTURE.md §7's base problem+json shape - <c>type</c>, <c>title</c>,
/// <c>status</c>, <c>detail</c>, <c>instance</c>, <c>code</c>, <c>traceId</c>,
/// <c>correlationId</c> - in ONE place, per §7's "every non-2xx (except 304)".
///
/// <para><b>traceId is the real W3C trace-id</b>, read from <c>Activity.Current</c>, which
/// OpenTelemetry populates for every request. §7: *"Always present, including on 500"*, and
/// *"enabling one-click log/OTel correlation"* - a generated-per-response id would satisfy the
/// shape and none of the purpose.</para>
///
/// <para><b>correlationId comes from the same scoped source the audit trail uses</b>
/// (<c>IAuditContext</c>), so a problem response and the AuditLog rows written while handling that
/// request carry the SAME id. Two independently-generated ids would look correct in a response body
/// and join to nothing.</para>
/// </summary>
public static class ProblemResponse
{
    private const string ContentType = "application/problem+json";

    /// <summary>§7: 500 carries "only type, title (generic), status, traceId, correlationId".</summary>
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

        // detail and code are optional in shape but not decoration: §7 says clients switch on
        // type/code, so a response that omits code forces them onto title/detail - the exact thing
        // §7 forbids. Emitted whenever the source had one.
        if (detail is not null) problem["detail"] = detail;
        if (code is not null) problem["code"] = code;

        return problem;
    }

    /// <summary>
    /// The 500 shape, deliberately built from nothing but the request context.
    ///
    /// <para>No exception message, no stack, no SQL - §7 is explicit, and the reason it is explicit
    /// is that an ORM exception message routinely carries a table name, a column, and sometimes the
    /// offending value. The generic title is a constant rather than anything derived from the
    /// exception, so there is no path by which internal text can reach the body.</para>
    /// </summary>
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

using Microsoft.Net.Http.Headers;
using MotsSupplierPortal.Api.Errors;

namespace MotsSupplierPortal.Api.Concurrency;

/// <summary>
/// §8.1's preconditions, applied as an endpoint filter so the rule is declared next to the route
/// rather than re-implemented in each handler.
/// </summary>
public static class ConcurrencyEndpoints
{
    /// <summary>
    /// Marks a mutation as requiring <c>If-Match</c>, per §8.1: "Mutating PUT/PATCH/transition POST
    /// on an existing resource MUST send If-Match".
    ///
    /// <para>Missing → <b>428</b> <c>IF_MATCH_REQUIRED</c>. Unparseable → <b>412</b>
    /// <c>ETAG_MISMATCH</c>, because a value that cannot be read cannot match the current version;
    /// answering 400 would tell the client its syntax was wrong when what matters is that its
    /// precondition failed, and 412 is the status the SPA already reconciles from.</para>
    ///
    /// <para>The STALE case is not decided here - only the database can say whether the row moved,
    /// so a well-formed version travels on to the save and comes back as a
    /// <c>DbUpdateConcurrencyException</c>, which the pipeline converts to the same 412.</para>
    /// </summary>
    public static RouteHandlerBuilder RequireIfMatch(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var header = http.Request.Headers[HeaderNames.IfMatch].ToString();

            if (string.IsNullOrWhiteSpace(header))
            {
                return Problem(http, StatusCodes.Status428PreconditionRequired,
                    ProblemTypes.PreconditionRequired, "If-Match is required.", "IF_MATCH_REQUIRED",
                    "This resource requires the ETag of the version you are editing, sent as If-Match.");
            }

            // "*" means "any current version", which is a valid If-Match under RFC 9110 but is
            // exactly the lost-update the guard exists to stop: it asserts nothing about what the
            // caller read. Refused rather than honoured.
            if (header.Trim() == "*" || !ETag.TryParse(header, out var expected))
            {
                return Problem(http, StatusCodes.Status412PreconditionFailed,
                    ProblemTypes.PreconditionFailed, "The precondition failed.", "ETAG_MISMATCH",
                    "The If-Match value is not an ETag this API issued.");
            }

            // Only an endpoint that declares the requirement binds the header to the write. A
            // stray If-Match on an endpoint outside §8.1's list must not quietly gate that write:
            // the caller was not promised a precondition there, and enforcing one would turn a
            // header it sent for some other resource into a 412 it cannot explain.
            http.Items[ExpectedVersionKey] = expected;

            return await next(context);
        });

    /// <summary>Where the validated expected version is published for the persistence layer.</summary>
    public const string ExpectedVersionKey = "MotsSupplierPortal.ExpectedRowVersion";

    /// <summary>
    /// Emits §8.1's ETag from whatever the handler returned, and turns a matching
    /// <c>If-None-Match</c> into a 304.
    ///
    /// <para>Reflective rather than generic over a DTO type, deliberately: the six aggregates return
    /// six unrelated DTOs through result unions of their own, and threading a type parameter through
    /// each would mean editing every handler's result mapping to expose the version a second time.
    /// The DTO already carries <c>RowVersion</c> for exactly this purpose, so the filter looks for
    /// that one property and does nothing when it is absent - which is what makes it safe to apply
    /// to an endpoint whose 404 branch returns no value at all.</para>
    /// </summary>
    public static RouteHandlerBuilder WithETag(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);

            if (result is IValueHttpResult { Value: { } value } && RowVersionOf(value) is { } rowVersion)
            {
                context.HttpContext.SetETag(rowVersion);

                // §8.1: "Conditional reads: If-None-Match -> 304 Not Modified (saves bandwidth on
                // polling, e.g. RFQ detail)." The body is dropped; the ETag stays, because a 304 is
                // still an answer about a specific version.
                if (context.HttpContext.IsNotModified(rowVersion)) return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return result;
        });

    /// <summary>
    /// T-030 split (3): puts the NEW version on a mutation's own response.
    ///
    /// <para>Separate from <see cref="WithETag"/> rather than reusing it, because that one also
    /// implements §8.1's conditional-read half - <c>If-None-Match → 304</c> - and a 304 on a POST that
    /// has already changed the row would be a lie about what happened.</para>
    ///
    /// <para>Why it matters here: the SPA drops its cached version the moment a mutation succeeds (a kept
    /// version would be stale by definition and turn the next save into an unexplainable 412). Without a
    /// fresh ETag on the response, a supplier editing two contacts in a row would hit 428 on the second
    /// until a re-read landed. Returning the version the write produced closes that window instead of
    /// racing a refetch.</para>
    /// </summary>
    public static RouteHandlerBuilder WithFreshETag(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);

            if (result is IValueHttpResult { Value: { } value } && RowVersionOf(value) is { } rowVersion)
            {
                context.HttpContext.SetETag(rowVersion);
            }

            return result;
        });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> VersionProperties = new();

    private static uint? RowVersionOf(object value)
    {
        // `long` as well as `uint`: SupplierDto widened the version years before §8.1 existed, to
        // keep it exactly representable in JSON. Both describe the same xmin.
        var property = VersionProperties.GetOrAdd(value.GetType(), static t =>
        {
            var p = t.GetProperty("RowVersion");
            return p?.PropertyType == typeof(uint) || p?.PropertyType == typeof(long) ? p : null;
        });

        return property?.GetValue(value) switch
        {
            uint v => v,
            long v and >= 0 and <= uint.MaxValue => (uint)v,
            _ => null,
        };
    }

    /// <summary>
    /// §8.1's read half: the current version goes out as a strong ETag, and a conditional read that
    /// already has it gets a 304 instead of a body.
    ///
    /// <para>The 304 is checked BEFORE the handler runs where possible - here it is checked after,
    /// because the version is only known once the resource is loaded. That still saves the body and
    /// the serialisation, which is what §8.1 asks it to save ("saves bandwidth on polling"); it does
    /// not save the query, and §8.1 does not claim it should.</para>
    /// </summary>
    public static void SetETag(this HttpContext context, uint rowVersion)
    {
        context.Response.Headers.ETag = ETag.Format(rowVersion);
    }

    /// <summary>True when the caller already holds this version and should be sent a bare 304.</summary>
    public static bool IsNotModified(this HttpContext context, uint rowVersion)
    {
        var header = context.Request.Headers[HeaderNames.IfNoneMatch].ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;
        if (header.Trim() == "*") return true;

        // A list of candidates is legal here, unlike If-Match: any one matching means not modified.
        return header.Split(',').Any(candidate => ETag.TryParse(candidate, out var v) && v == rowVersion);
    }

    private static IResult Problem(HttpContext http, int status, string type, string title, string code, string detail) =>
        new ProblemResult(ProblemResponse.Build(http, status, type, title, code, detail));

    private sealed record ProblemResult(System.Text.Json.Nodes.JsonObject Body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => ProblemResponse.WriteAsync(httpContext, Body);
    }
}

// The preconditions that stop two people overwriting each other, applied as route filters so the rule
// is declared next to the route rather than re-implemented in each handler.
//
// Four things live here: the filter that demands a precondition on a write, the two filters that put a
// version on a response, and the markers that make both facts enumerable from the route table.
//
//
// REQUIREIFMATCH: A WRITE MUST SAY WHICH VERSION IT READ
//
// Every update and every state change on an existing resource must send the version it is writing over.
//
// A missing header is a 428 saying the precondition is required. An unreadable one is a 412 saying the
// precondition failed, because a value that cannot be read cannot match the current version. Answering
// 400 would tell the client its syntax was wrong, when what matters is that its precondition failed, and
// 412 is the answer the interface already knows how to recover from.
//
// A wildcard is refused rather than honoured. It means "any current version", which is legal in the
// standard but is exactly the overwrite this guard exists to stop, because it asserts nothing about what
// the caller actually read.
//
// Whether the record has actually moved is not decided here. Only the database can answer that, so a
// well-formed version travels on to the save and comes back as a concurrency failure, which the pipeline
// turns into the same 412.
//
// Only a route that declares the requirement binds the header to its write. A stray precondition header
// on a route outside the contract must not quietly gate that write: the caller was not promised a
// precondition there, and enforcing one would turn a header it sent for some other resource into a
// failure it cannot explain.
//
//
// WITHETAG AND WITHFRESHETAG: A RESPONSE CARRIES ITS VERSION
//
// WithETag is the read half. The current version goes out as a tag, and a conditional read that already
// holds it gets a not-modified answer instead of a body. The body is dropped and the tag stays, because
// a not-modified answer is still an answer about a specific version.
//
// The check happens after the handler runs rather than before, because the version is only known once
// the resource has been loaded. That still saves the body and the work of serialising it, which is what
// the contract asks it to save. It does not save the query, and the contract does not claim it should.
//
// WithFreshETag is the write half: it puts the new version on a mutation's own response. It is separate
// from WithETag rather than a reuse of it, because that one also answers conditional reads, and a
// not-modified answer on a request that has already changed the record would be a lie about what
// happened.
//
// It matters because the interface drops its cached version the moment a write succeeds. A kept version
// would be stale by definition and turn the next save into an unexplainable failure. Without a fresh tag
// on the response, a supplier editing two contacts in a row would be refused on the second until a
// re-read landed. Returning the version the write produced closes that window rather than racing a
// refetch.
//
// Both filters find the version by looking for a property of that name on whatever the handler returned,
// rather than being typed to a particular response shape. That is deliberate: the several resources
// involved return unrelated shapes through result types of their own, and threading a type through each
// would mean editing every handler's result mapping to expose the version a second time. The response
// shapes already carry the version for exactly this purpose, so the filter looks for that one property
// and does nothing when it is absent, which is what makes it safe to put on a route whose not-found
// branch returns no value at all.
//
// It accepts the version as either of two number types. One response shape widened its version long
// before this contract existed, to keep it exactly representable in the wire format. Both describe the
// same underlying number.
//
//
// WHAT A TAG IS A TAG OF
//
// ResourceKey is the path plus the signed-in subject, and both halves are load-bearing.
//
// The path separates two different resources that happen to sit at the same version. The subject
// separates two callers at one path: the current-supplier route is a different resource for every
// supplier, and it is the path this defect was actually reported on, where one supplier's browser served
// another's profile because the two tags were identical.
//
// The query string is deliberately left out. A list's version belongs to its rows rather than to the page
// or the sort order the caller asked for, and folding the query in would issue a fresh tag for every
// combination and defeat the conditional read entirely.
//
// IsNotModified accepts a list of candidate tags, which is legal for a read unlike for a write: any one
// matching means not modified. It compares whole representations rather than just versions, because a
// not-modified answer tells the caller the body it already has is still right, and a body from an older
// build is not. A field added to a response shape moves no version, so comparing versions alone kept warm
// clients on the old shape indefinitely.
//
//
// THE TWO MARKERS
//
// RequiresIfMatchMetadata says "this route refuses a write without a precondition" and
// EmitsETagMetadata says "this route hands out one". They exist so both facts are enumerable.
//
// The contract has two halves declared in different places: a write demands a version, and some read has
// to have issued one for the path the client will write to. A sweep of the codebase found five separate
// writes where the second half was missing, each with a different cause, and every one was invisible
// until somebody pressed the button. The route compiled, the filter ran, and the only symptom was a
// refusal in a browser. A marker on the route table turns "did anyone check?" into a test.

namespace MotsSupplierPortal.Api.Concurrency;

using Microsoft.Net.Http.Headers;
using MotsSupplierPortal.Api.Errors;

public static class ConcurrencyEndpoints
{
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

            if (header.Trim() == "*" || !ETag.TryParse(header, out var expected))
            {
                return Problem(http, StatusCodes.Status412PreconditionFailed,
                    ProblemTypes.PreconditionFailed, "The precondition failed.", "ETAG_MISMATCH",
                    "The If-Match value is not an ETag this API issued.");
            }

            http.Items[ExpectedVersionKey] = expected;

            return await next(context);
        })
        .WithMetadata(RequiresIfMatchMetadata.Instance);

    public const string ExpectedVersionKey = "MotsSupplierPortal.ExpectedRowVersion";

    public static RouteHandlerBuilder WithETag(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);

            if (result is IValueHttpResult { Value: { } value } && RowVersionOf(value) is { } rowVersion)
            {
                context.HttpContext.SetETag(rowVersion);

                if (context.HttpContext.IsNotModified(rowVersion)) return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return result;
        })
        .WithMetadata(EmitsETagMetadata.Instance);

    public static RouteHandlerBuilder WithFreshETag(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);

            if (result is IValueHttpResult { Value: { } value } && RowVersionOf(value) is { } rowVersion)
            {
                context.HttpContext.SetETag(rowVersion);
            }

            return result;
        })
        .WithMetadata(EmitsETagMetadata.Instance);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> VersionProperties = new();

    private static uint? RowVersionOf(object value)
    {
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

    public static void SetETag(this HttpContext context, uint rowVersion)
    {
        context.Response.Headers.ETag = ETag.Format(rowVersion, ResourceKey(context));
    }

    private static string ResourceKey(HttpContext context)
    {
        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value
            ?? "anonymous";

        return $"{context.Request.Path.Value?.ToLowerInvariant()}|{subject}";
    }

    public static bool IsNotModified(this HttpContext context, uint rowVersion)
    {
        var header = context.Request.Headers[HeaderNames.IfNoneMatch].ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;
        if (header.Trim() == "*") return true;

        return header.Split(',').Any(candidate => ETag.MatchesCurrentRepresentation(candidate, rowVersion, ResourceKey(context)));
    }

    private static IResult Problem(HttpContext http, int status, string type, string title, string code, string detail) =>
        new ProblemResult(ProblemResponse.Build(http, status, type, title, code, detail));

    private sealed record ProblemResult(System.Text.Json.Nodes.JsonObject Body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => ProblemResponse.WriteAsync(httpContext, Body);
    }
}

public sealed class RequiresIfMatchMetadata
{
    public static readonly RequiresIfMatchMetadata Instance = new();

    private RequiresIfMatchMetadata() { }
}

public sealed class EmitsETagMetadata
{
    public static readonly EmitsETagMetadata Instance = new();

    private EmitsETagMetadata() { }
}

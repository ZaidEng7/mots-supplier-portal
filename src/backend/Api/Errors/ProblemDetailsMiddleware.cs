// Reshapes every failure response into one standard format, in one place.
//
// This is the piece that makes the rest of the folder work, and it is worth understanding before
// reading any endpoint, because it explains something that otherwise looks like a mess.
//
//
// WHY IT IS A MIDDLEWARE RATHER THAN 230 EDITS
//
// The contract requires the standard format on every failure response. The endpoints produce failure
// bodies in at least five different shapes: the framework's own validation response, a bare
// not-found with no body at all, an object carrying just an error name, one carrying an error and a
// message, and one carrying an error and details.
//
// Rewriting each site would have been around 230 edits whose correctness could only be checked by
// reading all of them, and it would leave the next new endpoint free to invent a sixth shape.
// Reshaping at the boundary makes conformance a property of the pipeline rather than a habit of
// whoever writes the next endpoint.
//
// So: when you see an endpoint return a plain object like an error name and a message, that is not a
// gap in the contract. It is the internal shorthand this middleware translates. Do not "fix" those
// endpoints to build the full format themselves; that is this file's job.
//
//
// WHAT SURVIVES THE TRANSLATION
//
// An error name in the body becomes the machine-readable code, upper-cased, because the contract
// requires that form. Every distinction the handlers already draw therefore survives, and the
// interface's existing checks keep a machine-readable home.
//
// Only an identifier becomes a code. Several handlers put the domain's own full sentence in that
// field, and the contract is explicit that the code is a short machine-stable name, so upper-casing a
// sentence would produce neither a code nor a readable message. A sentence goes to the detail
// instead, where human-readable explanation belongs, and the code falls back to one derived from the
// status. LooksLikeIdentifier is what tells the two apart: short, and no spaces.
//
// Anything else the body carried is kept as an extra field rather than dropped. The standard permits
// extra fields, and silently losing the list of missing fields would break the registration flow that
// reads it.
//
// A body that is already in the standard format passes through untouched, so the query-filter guards
// keep their exact behaviour and stop being special cases.
//
//
// WHAT IT BUFFERS, AND WHY ONLY THAT
//
// Only a response that turns out to be a failure is buffered. The first version buffered every
// response into memory, which breaks streaming: the audit export writes its rows straight to the wire
// precisely so that a large export never has to fit in memory, and holding it there to inspect a
// status it was never going to fail with is both a correctness and a memory regression.
//
// ErrorCapturingStream is what makes that work. It passes writes straight through while the response
// still looks successful and starts capturing once the status says otherwise.
//
//
// THE TWO CAUGHT EXCEPTIONS
//
// A malformed-request exception is thrown by the framework for an unreadable body, a field the API
// does not model, or a bad route value. It carries its own status, usually a 400, and swallowing it
// into a server error would turn a client's mistake into ours. A test asserts that refusal on an
// unmodelled field, so this is a regression guard as well as a correctness one.
//
// Anything else becomes a server error. The exception is logged in full, because losing the
// diagnostic would be the opposite mistake, and the response is built from the request alone, so
// there is no path from the exception to the body.
//
// Neither path can rewrite a response that has already started going out. Truncating one mid-flight
// would be worse than leaving its shape unconformed.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;

        using var buffer = new MemoryStream();
        var interceptor = new ErrorCapturingStream(context, originalBody, buffer);
        context.Response.Body = interceptor;

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException badRequest)
        {
            context.Response.Body = originalBody;
            if (!context.Response.HasStarted)
            {
                var status = badRequest.StatusCode;
                await ProblemResponse.WriteAsync(context, ProblemResponse.Build(
                    context, status, ProblemTypes.ForStatus(status), DefaultTitle(status),
                    code: status == 400 ? "MALFORMED_JSON" : null, detail: null));
            }
            return;
        }
        catch (Exception ex)
        {
            context.Response.Body = originalBody;
            logger.LogError(ex, "Unhandled exception handling {Method} {Path}", context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                await ProblemResponse.WriteAsync(context, ProblemResponse.ServerError(context));
            }
            return;
        }

        context.Response.Body = originalBody;
        await interceptor.FlushPassThroughAsync();
        buffer.Seek(0, SeekOrigin.Begin);

        if (context.Response.StatusCode < 400 || context.Response.HasStarted)
        {
            return;
        }

        var raw = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted);

        if (IsAlreadyProblemJson(context))
        {
            context.Response.ContentLength = null;
            await originalBody.WriteAsync(System.Text.Encoding.UTF8.GetBytes(raw), context.RequestAborted);
            return;
        }

        var problem = Conform(context, raw);
        context.Response.ContentLength = null;
        await ProblemResponse.WriteAsync(context, problem);
    }

    private static bool IsAlreadyProblemJson(HttpContext context) =>
        context.Response.ContentType?.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase) == true;

    private static JsonObject Conform(HttpContext context, string raw)
    {
        var status = context.Response.StatusCode;
        JsonObject? source = null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { source = JsonNode.Parse(raw) as JsonObject; }
            catch (JsonException) { /* Not JSON - a bare status with a text body. Shape it anyway. */ }
        }

        var code = CodeFrom(source, status);
        var detail = DetailFrom(source);
        var problem = ProblemResponse.Build(context, status, ProblemTypes.ForStatus(status),
            TitleFrom(source, status), code, detail);

        CarryExtensions(source, problem);
        return problem;
    }

    private static string CodeFrom(JsonObject? source, int status)
    {
        if (source?["error"]?.GetValue<string>() is { Length: > 0 } error && LooksLikeIdentifier(error))
        {
            return error.ToUpperInvariant().Replace('-', '_');
        }

        return status switch
        {
            400 => "MALFORMED_REQUEST",
            401 => "TOKEN_INVALID",
            403 => "PERMISSION_DENIED",
            404 => "RESOURCE_NOT_FOUND",
            409 => "CONFLICT",
            412 => "ETAG_MISMATCH",
            413 => "FILE_TOO_LARGE",
            415 => "MIME_NOT_ALLOWED",
            422 => "VALIDATION_FAILED",
            428 => "IF_MATCH_REQUIRED",
            429 => "RATE_LIMIT_EXCEEDED",
            503 => "DB_UNAVAILABLE",
            _ => "INTERNAL_ERROR",
        };
    }

    private static bool LooksLikeIdentifier(string value) =>
        value.Length <= 64 && !value.Any(char.IsWhiteSpace);

    private static string? DetailFrom(JsonObject? source)
    {
        if (source?["message"]?.GetValue<string>() is { Length: > 0 } message) return message;
        if (source?["detail"]?.GetValue<string>() is { Length: > 0 } detail) return detail;

        if (source?["error"]?.GetValue<string>() is { Length: > 0 } sentence && !LooksLikeIdentifier(sentence))
        {
            return sentence;
        }

        return null;
    }

    private static string TitleFrom(JsonObject? source, int status) =>
        source?["title"]?.GetValue<string>() ?? DefaultTitle(status);

    private static string DefaultTitle(int status) => status switch
    {
        400 => "The request could not be understood.",
        401 => "Authentication is required.",
        403 => "You do not have permission to perform this action.",
        404 => "The requested resource was not found.",
        409 => "The request conflicts with the current state of the resource.",
        412 => "A precondition failed.",
        413 => "The payload is too large.",
        415 => "The media type is not supported.",
        422 => "One or more validation errors occurred.",
        428 => "A precondition header is required.",
        429 => "Too many requests.",
        503 => "A dependency is unavailable.",
        _ => "An unexpected error occurred.",
    };

    private static void CarryExtensions(JsonObject? source, JsonObject problem)
    {
        if (source is null) return;

        foreach (var member in source)
        {
            if (member.Key is "error" or "message" or "title" or "status" or "type" or "detail" or "instance") continue;
            if (problem.ContainsKey(member.Key)) continue;
            problem[member.Key] = member.Value?.DeepClone();
        }
    }

    private sealed class ErrorCapturingStream(HttpContext context, Stream passThrough, MemoryStream capture) : Stream
    {
        private bool? _capturing;

        private bool Capturing => _capturing ??= context.Response.StatusCode >= 400;

        public Task FlushPassThroughAsync() => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Capturing) capture.Write(buffer, offset, count);
            else passThrough.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Capturing) await capture.WriteAsync(buffer, cancellationToken);
            else await passThrough.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { if (!Capturing) passThrough.Flush(); }
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Capturing ? Task.CompletedTask : passThrough.FlushAsync(cancellationToken);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// Conforms every non-2xx response to §7's problem+json shape, in one place.
///
/// <para><b>Why a response-shaping middleware rather than editing ~230 call sites.</b> §7 says
/// "every non-2xx (except 304)", and the endpoints produce error bodies in at least five shapes
/// today: ASP.NET's ValidationProblem, bare <c>Results.NotFound()</c> with no body at all,
/// <c>{ error }</c>, <c>{ error, message }</c>, <c>{ error, details }</c>. Rewriting each site would
/// be ~230 edits whose correctness could only be checked by reading all of them, and would leave
/// the next new endpoint free to invent a sixth shape. Shaping at the boundary makes conformance a
/// property of the pipeline instead of a habit of authors.</para>
///
/// <para><b>Existing error identifiers are preserved, not discarded.</b> A body carrying
/// <c>{ "error": "invalid_state" }</c> becomes <c>"code": "INVALID_STATE"</c> - §7 requires
/// SCREAMING_SNAKE - so every distinction the handlers already draw survives the migration, and the
/// SPA's existing sentinels keep a machine-readable home. Anything else the body carried
/// (<c>message</c>, <c>details</c>, <c>missingFields</c>) is preserved as an extension member
/// rather than dropped: RFC 9457 permits extensions, and silently losing
/// <c>missingFields</c> would break the onboarding submit flow that reads it.</para>
///
/// <para><b>Bodies that are already problem+json pass through untouched</b>, so the filter guards
/// (§6.2's unknown-filter, §6.3's unknown sort key, the page cap) keep their exact behaviour and
/// stop being special cases: they now produce the same media type as everything else and are simply
/// not rewritten twice.</para>
/// </summary>
public sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;

        // Buffer ONLY what turns out to be an error. The first version buffered every response into
        // a MemoryStream, which breaks streaming endpoints - the audit export writes an
        // IAsyncEnumerable straight to the wire precisely so a large export never materialises, and
        // holding it in memory to inspect a status it was never going to fail with is both a
        // correctness and a memory regression. This stream passes writes straight through until it
        // sees a non-2xx status, and only then starts capturing.
        using var buffer = new MemoryStream();
        var interceptor = new ErrorCapturingStream(context, originalBody, buffer);
        context.Response.Body = interceptor;

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException badRequest)
        {
            // ASP.NET throws this for a malformed body, an unmodelled field under strict binding, a
            // bad route value. It CARRIES its own status (usually 400) and swallowing it into a 500
            // would turn a client error into a server one - which is both wrong and a regression:
            // ProfilePatchSemanticsTests asserts NFR-SEC-005's 400 on an unmodelled field.
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
            // §7: a 500 carries no stack, SQL or internal message. The exception is LOGGED in full -
            // losing the diagnostic entirely would be the opposite mistake - and the response is
            // built from the request context alone, so there is no path from ex to the body.
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

        // 304 is excluded by §7 by name, and a 2xx is not an error. A response already committed to
        // the wire cannot be rewritten, and silently truncating it would be worse than leaving the
        // shape unconformed.
        if (context.Response.StatusCode < 400 || context.Response.HasStarted)
        {
            // Already written straight through by the interceptor; nothing captured.
            return;
        }

        var raw = await new StreamReader(buffer).ReadToEndAsync();

        if (IsAlreadyProblemJson(context))
        {
            context.Response.ContentLength = null;
            await originalBody.WriteAsync(System.Text.Encoding.UTF8.GetBytes(raw));
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

    /// <summary>
    /// <c>{ "error": "invalid_state" }</c> → <c>INVALID_STATE</c>. ASP.NET's ValidationProblem
    /// carries no error field, so a 422 from it falls back to the documented VALIDATION_FAILED
    /// (§7.2's own worked example uses exactly that code).
    /// </summary>
    private static string CodeFrom(JsonObject? source, int status)
    {
        // Only an IDENTIFIER becomes a code. Several handlers put the domain's own sentence in
        // `error` ("Cannot deactivate from lifecycle state 'Active'; only 'Suspended' is valid…"),
        // and §7 is explicit that code is a "machine-stable app error code (SCREAMING_SNAKE)" -
        // uppercasing a sentence produces neither a code nor a readable message. A sentence goes to
        // `detail`, where §7 puts human-readable explanation, and the code falls back to the status.
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

    /// <summary>An identifier: short, no whitespace - the shape `invalid_state` has and a domain
    /// sentence does not.</summary>
    private static bool LooksLikeIdentifier(string value) =>
        value.Length <= 64 && !value.Any(char.IsWhiteSpace);

    private static string? DetailFrom(JsonObject? source)
    {
        if (source?["message"]?.GetValue<string>() is { Length: > 0 } message) return message;
        if (source?["detail"]?.GetValue<string>() is { Length: > 0 } detail) return detail;

        // The sentence-shaped `error` case: it is the explanation, so it lands in detail rather
        // than being lost. NFR-CMP-003/BRULE-097 require the caller to be told which state was
        // required, and that text only exists here.
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

    /// <summary>
    /// Members the handlers already return and callers already read - notably
    /// <c>missingFields</c> on the onboarding submit 422, and ASP.NET's own <c>errors</c> map -
    /// are carried through as RFC 9457 extension members. Dropping them would conform the shape by
    /// breaking the behaviour.
    /// </summary>
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

    /// <summary>
    /// Passes writes straight to the real body while the response looks successful, and captures
    /// them once the status says otherwise. Keeps streaming endpoints streaming while still letting
    /// an error body be rewritten into §7's shape.
    /// </summary>
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

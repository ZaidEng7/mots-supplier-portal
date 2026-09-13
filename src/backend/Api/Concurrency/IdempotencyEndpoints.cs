// The header that makes a retry safe, so a supplier double-clicking Submit cannot submit twice.
//
// A client sends its own key with a non-repeatable request. The first attempt does the work and its
// answer is recorded against that key; a retry with the same key replays the recorded answer instead of
// doing the work again. Records are kept for a day, a retention the contract itself tags as an
// assumption.
//
//
// HOW THE RESERVATION WORKS
//
// The filter records the key with an empty answer before the handler runs, and relies on the database's
// own uniqueness rather than a lock. Two concurrent retries both try to record it; the database lets
// exactly one through and the loser sees a duplicate-key failure. That is what refuses the second click
// without the read-then-write race a naive check-then-insert would have.
//
// A key already in flight is a conflict rather than a wait. If a record exists with no answer yet, either
// the first request is still running or it died part-way. Blocking would hold a request open on a bet
// about somebody else's progress, and replaying nothing would be a lie. The contract's own answer for a
// key that cannot be honoured is a conflict, and a client that receives one retries with a new key.
//
// The same key sent with a different request is also a conflict. Answering with the stored response there
// would hand a client the outcome of a call it did not make. The fingerprint is what tells the two apart:
// a hash of the method, the path and the body. It is hashed rather than stored raw, because a body can
// carry a price or a rejection reason and this table is not the place for either.
//
// Only a success is worth replaying. A client error is the caller's to fix and re-send, and storing it
// would pin them to their own mistake for a day with no way to correct the request.
//
//
// WHAT THIS DELIBERATELY DOES NOT DO
//
// The reservation and the handler's own write are not in one transaction. If the process dies after the
// handler commits but before the answer is recorded, the record stays in flight and a retry gets a
// conflict rather than the original answer.
//
// The work still happened exactly once, which is the property that matters, but the client learns it by a
// conflict instead of a replay. Making the two atomic means this filter owning the handler's transaction,
// which changes every handler's contract. Recorded as the remaining half rather than half-built.
//
//
// RENDERING THE ANSWER ONCE
//
// The handler's answer is rendered here, once, and the rendered bytes are what both the client and the
// store receive.
//
// The first version executed the answer into a buffer to capture it and then returned it, so the
// framework executed it a second time to write the real response. That happened to work for a plain
// success, which merely re-serialised, but an answer with any side effect, or one that cannot be
// executed twice, would have broken in a way nothing here would catch.
//
// The response stream is swapped for a buffer and restored in a finally block, so a handler that throws
// cannot leave the real stream replaced.
//
// The content type the handler chose is carried out with the bytes. It used to be hardcoded to plain
// JSON, and that silently flattened every error code these routes produced: a standard problem response
// re-sent as plain JSON is no longer recognised as already-conformed, so the middleware rebuilds it and
// re-derives the code from the status. That turned an illegal-transition code into a bare conflict while
// leaving the state fields in place, which is the worst of both, since the extra fields say a transition
// was refused and the code a client switches on says nothing. Found when lapsing a draft bid made
// "submit a bid that has moved on" reachable on one of these routes for the first time.
//
//
// TWO DELIBERATE CANCELLATION CHOICES
//
// Recording the answer ignores the client disconnecting. By that point the handler has already committed
// its work, and abandoning the capture would leave the record in flight with no stored answer, so the
// very retry that follows a dropped connection, which is the case this whole mechanism exists for, would
// get a conflict instead of the replay it should get. The read there is from a buffer already fully
// populated, so there is nothing to wait on and nothing to cancel.
//
// Computing the fingerprint does respect it, because that reads the request body off the wire, so a client
// that has gone away is a read that should stop rather than one that must finish. Nothing has been
// committed at that point, so abandoning costs nothing.
//
// The answer is recorded through a fresh database context rather than the request's own, because that one
// has just been used by the handler and may hold tracked records a second save would write again.

namespace MotsSupplierPortal.Api.Concurrency;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Idempotency;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class IdempotencyEndpoints
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var key = http.Request.Headers[HeaderName].ToString();

            if (string.IsNullOrWhiteSpace(key))
            {
                return Problem(http, StatusCodes.Status428PreconditionRequired,
                    ProblemTypes.PreconditionRequired, "Idempotency-Key is required.",
                    "IDEMPOTENCY_KEY_REQUIRED",
                    "This transition requires a client-generated Idempotency-Key so a retry cannot repeat it.");
            }

            var scope = http.RequestServices.GetRequiredService<IScopeContext>();
            if (scope.UserId is not { } userId) return await next(context);

            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var fingerprint = await FingerprintAsync(http);

            var existing = await db.IdempotencyRecords
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, http.RequestAborted);

            if (existing is not null)
            {
                if (existing.RequestFingerprint != fingerprint)
                {
                    return Problem(http, StatusCodes.Status409Conflict,
                        ProblemTypes.IdempotencyConflict, "The Idempotency-Key was reused.",
                        "IDEMPOTENCY_KEY_REUSED",
                        "This Idempotency-Key was already used for a different request.");
                }

                if (existing.ResponseStatusCode is { } storedStatus)
                {
                    http.Response.Headers[ReplayedHeaderName] = "true";
                    return Results.Content(existing.ResponseBody ?? string.Empty, "application/json", Encoding.UTF8, storedStatus);
                }

                return Problem(http, StatusCodes.Status409Conflict,
                    ProblemTypes.IdempotencyConflict, "The original request is still in flight.",
                    "IDEMPOTENCY_KEY_IN_FLIGHT",
                    "A request with this Idempotency-Key has not finished. Retry with a new key.");
            }

            var record = new IdempotencyRecord
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Key = key,
                RequestFingerprint = fingerprint,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(Retention),
            };

            db.IdempotencyRecords.Add(record);
            try
            {
                await db.SaveChangesAsync(http.RequestAborted);
            }
            catch (DbUpdateException)
            {
                db.Entry(record).State = EntityState.Detached;
                return Problem(http, StatusCodes.Status409Conflict,
                    ProblemTypes.IdempotencyConflict, "The original request is still in flight.",
                    "IDEMPOTENCY_KEY_IN_FLIGHT",
                    "A request with this Idempotency-Key is already being processed.");
            }

            var result = await next(context);

            var (status, body, contentType) = await CaptureAsync(http, result);
            await RecordOutcomeAsync(http, record, status, body);

            return body is null
                ? Results.StatusCode(status)
                : Results.Content(body, contentType, Encoding.UTF8, status);
        });

    private static async Task RecordOutcomeAsync(HttpContext http, IdempotencyRecord record, int status, string? body)
    {
        if (status is < 200 or > 299) return;

        using var freshScope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = freshScope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.IdempotencyRecords
            .Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.ResponseStatusCode, status)
                .SetProperty(r => r.ResponseBody, body), CancellationToken.None);
    }

    private static async Task<(int Status, string? Body, string ContentType)> CaptureAsync(HttpContext http, object? result)
    {
        if (result is not IResult typed) return (http.Response.StatusCode, null, "application/json");

        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await typed.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = original;
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8);

        var body = await reader.ReadToEndAsync(CancellationToken.None);

        var contentType = string.IsNullOrEmpty(http.Response.ContentType)
            ? "application/json"
            : http.Response.ContentType;
        return (http.Response.StatusCode, string.IsNullOrEmpty(body) ? null : body, contentType);
    }

    private static async Task<string> FingerprintAsync(HttpContext http)
    {
        http.Request.EnableBuffering();
        http.Request.Body.Position = 0;
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);

        var body = await reader.ReadToEndAsync(http.RequestAborted);
        http.Request.Body.Position = 0;

        var material = $"{http.Request.Method}\n{http.Request.Path}\n{body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static IResult Problem(
        HttpContext http, int status, string type, string title, string code, string detail) =>
        Results.Problem(statusCode: status, type: type, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

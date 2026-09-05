using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Idempotency;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Api.Concurrency;

/// <summary>
/// T-053/§8.2: <c>Idempotency-Key</c>, so a supplier double-clicking Submit cannot submit twice.
/// </summary>
public static class IdempotencyEndpoints
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    /// <summary>§8.2's 24-hour retention, tagged <c>[ASSUMPTION]</c> in the document itself.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>
    /// Requires and honours <c>Idempotency-Key</c> on a non-idempotent POST.
    ///
    /// <para><b>Reservation by unique index, not by lock.</b> The filter INSERTs a record with the
    /// response columns null before the handler runs. Two concurrent retries both try; Postgres lets
    /// exactly one through and the loser sees a duplicate-key violation. That is what refuses the second
    /// click without a read-then-write race, which is the shape a naive "check then insert" would
    /// have.</para>
    ///
    /// <para><b>An IN-FLIGHT key is a 409, not a wait.</b> If a record exists with no response yet,
    /// either the first request is still running or it died mid-flight. Blocking would hold a request
    /// thread on a bet about someone else's progress; replaying nothing would be a lie. §8.2's own
    /// answer for a key that cannot be honoured is a conflict, and a client that gets one retries with a
    /// new key.</para>
    ///
    /// <para><b>What this does NOT do, stated plainly.</b> The reservation and the handler's own write
    /// are not in one transaction. If the process dies after the handler commits but before the response
    /// is recorded, the record stays in-flight and a retry gets a 409 rather than the original
    /// response - the work happened exactly once, which is the property that matters, but the client
    /// learns it by a conflict instead of a replay. Making the two atomic means the filter owning the
    /// handler's transaction, which is a change to every handler's contract; recorded as the remaining
    /// half rather than half-built. See DECISIONS-TAKEN.md D-29.</para>
    /// </summary>
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
                // §8.2.4: same key, different request. Answering the stored response here would hand a
                // client the outcome of a call it did not make.
                if (existing.RequestFingerprint != fingerprint)
                {
                    return Problem(http, StatusCodes.Status409Conflict,
                        ProblemTypes.IdempotencyConflict, "The Idempotency-Key was reused.",
                        "IDEMPOTENCY_KEY_REUSED",
                        "This Idempotency-Key was already used for a different request.");
                }

                // §8.2.3: replay verbatim.
                if (existing.ResponseStatusCode is { } status)
                {
                    http.Response.Headers[ReplayedHeaderName] = "true";
                    return Results.Content(existing.ResponseBody ?? string.Empty, "application/json", Encoding.UTF8, status);
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
                // Lost the race on the unique index: another request reserved this key between the read
                // above and this insert. That is the double-click, and it is refused rather than run.
                db.Entry(record).State = EntityState.Detached;
                return Problem(http, StatusCodes.Status409Conflict,
                    ProblemTypes.IdempotencyConflict, "The original request is still in flight.",
                    "IDEMPOTENCY_KEY_IN_FLIGHT",
                    "A request with this Idempotency-Key is already being processed.");
            }

            var result = await next(context);

            await RecordOutcomeAsync(http, record, result);
            return result;
        });

    /// <summary>
    /// Stores what the handler answered, so the next retry replays it.
    ///
    /// <para>Executed via a fresh scope's context: the request's own <c>AppDbContext</c> has just been
    /// used by the handler and may hold tracked entities whose state a second SaveChanges would flush
    /// again.</para>
    /// </summary>
    private static async Task RecordOutcomeAsync(HttpContext http, IdempotencyRecord record, object? result)
    {
        var (status, body) = await CaptureAsync(http, result);

        // Only a SUCCESS is worth replaying. A 4xx is the caller's to fix and re-send, and storing it
        // would pin a client to its own mistake for 24 hours with no way to correct the request.
        if (status is < 200 or > 299) return;

        using var freshScope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = freshScope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.IdempotencyRecords
            .Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.ResponseStatusCode, status)
                .SetProperty(r => r.ResponseBody, body), CancellationToken.None);
    }

    /// <summary>Renders the handler's result so it can be stored and later replayed byte-for-byte.</summary>
    private static async Task<(int Status, string? Body)> CaptureAsync(HttpContext http, object? result)
    {
        if (result is not IResult typed) return (http.Response.StatusCode, null);

        var original = http.Response.Body;
        using var buffer = new MemoryStream();
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
        var body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
        return (http.Response.StatusCode, string.IsNullOrEmpty(body) ? null : body);
    }

    /// <summary>§8.2.1's fingerprint: <c>hash of method+path+body</c>. Hashed, not stored raw - a body
    /// can carry a price or a reason, and this table is not the place for either.</summary>
    private static async Task<string> FingerprintAsync(HttpContext http)
    {
        http.Request.EnableBuffering();
        http.Request.Body.Position = 0;
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        http.Request.Body.Position = 0;

        var material = $"{http.Request.Method}\n{http.Request.Path}\n{body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static IResult Problem(
        HttpContext http, int status, string type, string title, string code, string detail) =>
        Results.Problem(statusCode: status, type: type, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

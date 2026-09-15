// The order the middleware runs in. The order is the whole content of this file, and two of the
// positions here were bugs before they were decisions.
//
//
// COMPRESSION IS OUTERMOST, ABOVE THE FAILURE RESHAPING
//
// It used to sit near the routes, underneath the reshaping. That middleware swaps the response stream so
// it can rewrite a handler's short failure body into the standard format, and with compression inside
// it the rewritten bytes were written past a layer that had already declared the response compressed.
//
// The result: every failure response the middleware reshaped arrived at a browser with a compression
// header and a body that was not compressed, which the browser reports as a decoding failure and hands
// the interface as an empty body.
//
// Reproduced on a password-reset refusal, which the change that found this never touched: the full
// failure body from a plain request, and nothing at all from the same request asking for compression.
// Every browser asks for compression, so in practice the interface could not read the failure code on
// any reshaped response and fell back to generic wording.
//
// Ordering it first makes compression the last thing to touch the bytes, which is exactly what the
// middleware's own documented requirement asks for. Successful responses were never affected, which is
// why this survived: they are not reshaped.
//
//
// THE CORRELATION IDENTIFIER IS SECOND, THEREFORE OUTSIDE EVERYTHING ELSE
//
// The response header has to be registered before any middleware can start the response, and the
// identifier has to be adopted before the first audit row is written, which the failure handler below
// can itself cause.
//
//
// FAILURE RESHAPING IS BEFORE THE STALE-WRITE HANDLER AND BEFORE THE ROUTES
//
// So it is outermost among the error-shaping middleware and therefore sees, and conforms, whatever they
// produce, including the refusal the handler below writes.
//
//
// THE STALE-WRITE HANDLER
//
// A version mismatch on any write surfaces from the data layer as a concurrency exception. Before this,
// only two supplier writes translated it into a documented answer, through a helper wired into those
// two handlers, and every other record's writes let it reach the caller as an unhandled server error.
// An audit found it.
//
// Handled once, here, rather than by wrapping every handler's own save. A lost update is a failed
// precondition rather than a conflict: the older answer was a conflict with a short body the interface
// string-matched in five places, and conflict now means only what the contract says it means, which is
// an illegal state change or a duplicate.
//
// By the time this runs, the route filter has already refused a missing or unreadable precondition, so
// reaching here means the caller sent a well-formed version and the database found the record had
// moved. That is the genuinely stale case, and the only one the database can decide.
//
//
// SECURITY HEADERS ON EVERY RESPONSE
//
// Applied before the routes so they cover failure responses too, not only successful ones.
//
// The caching rules are the important part. The authentication, registration, document and review paths
// are never stored at all.
//
// Every other signed-in read gets a private no-cache rule and a note that the answer depends on who
// asked. That combination used to be absent, while those responses carried a version tag, and that is
// the one combination a cache is allowed to treat as its own business: a stored response with a
// validator and no explicit freshness may be reused without asking anybody. The browser then keys the
// entry on the address alone, and the authorisation header is not something it has any reason to
// consider unless the response says so.
//
// So two people signing into the same browser shared one entry per address. Found by a supplier who
// registered a new account, opened their own profile and was shown the previous account's: legal name,
// registration number, and an approved status that was not theirs. Reproduced exactly, and the server
// was never asked.
//
// No-cache rather than no-store, deliberately. Conditional reads are built on those version tags, and
// no-store would delete that feature to fix this bug. No-cache keeps the stored copy and forbids using
// it without checking first, so the not-modified path still works and every reuse passes through the
// token check.
//
//
// THE REST OF THE ORDER
//
// Browser origins, then the secure-connection redirect, then rate limiting, then authentication, then
// authorisation. Rate limiting sits before authentication deliberately, so an unauthenticated flood is
// refused before anything expensive happens.

namespace MotsSupplierPortal.Api.Startup;

using Microsoft.EntityFrameworkCore;
using Serilog;
using MotsSupplierPortal.Api.Errors;

internal static class ApiPipeline
{
    internal static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (ForwardedHeadersRegistration.TrustsProxyHeaders(app.Configuration))
        {
            app.UseForwardedHeaders();
        }

        app.UseResponseCompression();

        app.UseMiddleware<MotsSupplierPortal.Api.Observability.CorrelationIdMiddleware>();

        app.UseMiddleware<MotsSupplierPortal.Api.Errors.ProblemDetailsMiddleware>();

        app.UseSerilogRequestLogging();

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    await ProblemResponse.WriteAsync(context, ProblemResponse.Build(
                        context, StatusCodes.Status412PreconditionFailed, ProblemTypes.PreconditionFailed,
                        "The precondition failed.", "ETAG_MISMATCH",
                        "This resource changed after you loaded it. Refetch it and reapply your change."));
                }
            }
        });

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
            headers.Append("Content-Security-Policy",
                "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
                "object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
            headers.Append("X-Content-Type-Options", "nosniff");
            headers.Append("X-Frame-Options", "DENY");
            headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            headers.Append("Cross-Origin-Opener-Policy", "same-origin");
            headers.Append("Cross-Origin-Resource-Policy", "same-origin");

            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/v1/registrations", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/v1/documents", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/v1/review", StringComparison.OrdinalIgnoreCase))
            {
                headers.Append("Cache-Control", "no-store");
            }
            else if (path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
            {
                headers.Append("Cache-Control", "private, no-cache");
                headers.Append("Vary", "Authorization");
            }

            await next();
        });

        app.UseCors();
        app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}

// The second rate limit on the sign-in and registration surfaces: one per email address, on top of
// the one per network address.
//
// The security rules want both dimensions. Signing in needs a limit per network address and per
// account; registering, resending a verification and resetting a password need one per network address
// and per target. The framework's own rate limiting supports only one dimension per route, so this is
// the second one, keyed by the normalised identity in the request body rather than by the connection.
// Both must pass, and each endpoint checks this one explicitly alongside the per-address policy.
//
// Registration has a tighter budget than everything else. Every surface used to share one hardcoded
// budget regardless of how consequential the request was. A sign-in attempt costs a password
// comparison; a registration attempt writes two rows and queues an email. Everything not listed keeps
// the previous default.
//
// The surface tag is part of the key so that the same email does not share a budget across unrelated
// surfaces.
//
// RateLimitResults is the refusal: a 429 carrying the header that says how long to wait, as the
// security rules require.

namespace MotsSupplierPortal.Api.Authorization;

using System.Threading.RateLimiting;
using MotsSupplierPortal.Infrastructure.Observability;

public sealed class PerTargetRateLimiter(AppMetrics metrics) : IDisposable
{
    private static readonly Dictionary<string, (int PermitLimit, TimeSpan Window)> SurfaceLimits = new()
    {
        ["register"] = (5, TimeSpan.FromMinutes(1)),
    };

    private static readonly (int PermitLimit, TimeSpan Window) DefaultLimit = (10, TimeSpan.FromMinutes(1));

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(key =>
    {
        var surface = key.Split(':', 2)[0];
        var (permitLimit, window) = SurfaceLimits.GetValueOrDefault(surface, DefaultLimit);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            Window = window,
            PermitLimit = permitLimit,
            QueueLimit = 0,
        });
    });

    public bool TryAcquire(string surface, string target)
    {
        var acquired = _limiter.AttemptAcquire($"{surface}:{target}").IsAcquired;
        if (!acquired)
        {
            metrics.RateLimitRejections.Add(1,
                new KeyValuePair<string, object?>("surface", surface),
                new KeyValuePair<string, object?>("layer", "per-target"));
        }
        return acquired;
    }

    public void Dispose() => _limiter.Dispose();
}

public static class RateLimitResults
{
    public static IResult TooManyRequests(HttpContext httpContext, int retryAfterSeconds = 60)
    {
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}

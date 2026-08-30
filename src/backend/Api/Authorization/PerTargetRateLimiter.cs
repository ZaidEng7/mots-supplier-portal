using System.Threading.RateLimiting;
using MotsSupplierPortal.Infrastructure.Observability;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// SECURITY-ARCHITECTURE.md §5.1: login needs per-IP + per-account limiting, and
/// register/resend/reset need per-IP + per-target - the ASP.NET Core rate-limiting middleware's
/// endpoint policy selection only supports one partition dimension (client IP) at a time, so this
/// is the second dimension, keyed by the normalized identity in the request body rather than the
/// connection. Both must pass for a request to proceed; this one is checked explicitly by each
/// endpoint alongside the existing "auth-strict" per-IP policy.
/// </summary>
public sealed class PerTargetRateLimiter(AppMetrics metrics) : IDisposable
{
    // NFR-SEC-009: every surface shared one hardcoded 10/min budget
    // regardless of how consequential a request actually is. A login attempt costs a password
    // hash comparison; a registration attempt writes a Supplier + AppUser row and enqueues an
    // email - registration gets its own, tighter budget instead. Everything not listed here keeps
    // the previous 10/min default, unchanged.
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

    /// <param name="surface">A short tag for the endpoint (e.g. "login", "register") so the same
    /// email doesn't share a budget across unrelated surfaces.</param>
    /// <param name="target">The normalized identity being targeted (email).</param>
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
    /// <summary>SECURITY-ARCHITECTURE.md §5.1: "Rate-limit responses use 429 with Retry-After".</summary>
    public static IResult TooManyRequests(HttpContext httpContext, int retryAfterSeconds = 60)
    {
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}

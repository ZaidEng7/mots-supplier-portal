using System.Threading.RateLimiting;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// SECURITY-ARCHITECTURE.md §5.1: login needs per-IP + per-account limiting, and
/// register/resend/reset need per-IP + per-target - the ASP.NET Core rate-limiting middleware's
/// endpoint policy selection only supports one partition dimension (client IP) at a time, so this
/// is the second dimension, keyed by the normalized identity in the request body rather than the
/// connection. Both must pass for a request to proceed; this one is checked explicitly by each
/// endpoint alongside the existing "auth-strict" per-IP policy.
/// </summary>
public sealed class PerTargetRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0,
        }));

    /// <param name="surface">A short tag for the endpoint (e.g. "login", "register") so the same
    /// email doesn't share a budget across unrelated surfaces.</param>
    /// <param name="target">The normalized identity being targeted (email).</param>
    public bool TryAcquire(string surface, string target) =>
        _limiter.AttemptAcquire($"{surface}:{target}").IsAcquired;

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

// The application's own business metrics, as distinct from the per-route HTTP figures the framework's
// instrumentation already emits.
//
// That instrumentation covers request counts, latency by endpoint and error rates for every route automatically,
// as a standard query over its own duration histogram tagged by status. Nothing here duplicates it.
//
// What is here is the two things it genuinely cannot see. A rejected request from the rate limiter, which is a
// security-relevant event rather than a fact about an HTTP shape, now that rate limiting is a real defended
// surface. And the outbox backlog, which is a fact about a database table rather than about a request.
//
// Deliberately small. The stated goal was to be able to see what is happening in production, not to build a fully
// instrumented observability platform.

namespace MotsSupplierPortal.Infrastructure.Observability;

using System.Diagnostics.Metrics;

public sealed class AppMetrics : IDisposable
{
    public const string MeterName = "MotsSupplierPortal";

    private readonly Meter _meter;

    public AppMetrics()
    {
        _meter = new Meter(MeterName);
        RateLimitRejections = _meter.CreateCounter<long>(
            "mots.rate_limit.rejections",
            unit: "{rejection}",
            description: "Requests rejected by a rate limit, tagged by surface (login, register, ...) and layer (per-ip, per-target).");
    }

    public Counter<long> RateLimitRejections { get; }

    public Meter Meter => _meter;

    public void Dispose() => _meter.Dispose();
}

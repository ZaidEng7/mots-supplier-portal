using System.Diagnostics.Metrics;

namespace MotsSupplierPortal.Infrastructure.Observability;

/// <summary>
/// Task #16/NFR-OBS-006: the app's own business metrics, distinct from the free per-route HTTP
/// count/duration/status ASP.NET Core's OpenTelemetry instrumentation already emits (wired in
/// Program.cs's .WithMetrics(m => m.AddAspNetCoreInstrumentation())) - that instrumentation covers
/// "request counts/latency by endpoint" and "error rates" (a 4xx/5xx ratio is a standard query over
/// its http.server.request.duration histogram, tagged by http.response.status_code) for every
/// endpoint automatically, not a hand-picked few, so nothing here duplicates it.
///
/// <para>What's here is the two things OTel's built-in instrumentation genuinely cannot see:
/// rate-limit rejections (a security-relevant event, not an HTTP-shape fact - task #4 made rate
/// limiting a real defended surface, worth watching for someone actually being throttled at scale)
/// and the Outbox backlog gauge (Infrastructure/Suppliers/OutboxDispatcher.cs), which is a fact
/// about a database table, not a fact about a request.</para>
///
/// <para>Deliberately small. The goal stated for this item was "we can see what's happening in
/// production", not a fully-instrumented observability platform - one Meter, one Counter here, one
/// ObservableGauge added alongside the Outbox dispatcher.</para>
/// </summary>
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

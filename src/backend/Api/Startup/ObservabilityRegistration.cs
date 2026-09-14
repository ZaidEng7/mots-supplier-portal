// Logging, traces, metrics and the two health probes.
//
//
// WHY THE CULTURE IS PINNED
//
// Every thread in this process formats and parses numbers and dates the same way, whatever the host's
// own locale says.
//
// This service writes formatted values into places where the host's locale has no business being: an
// append-only audit row, which a database trigger makes impossible to correct afterwards; the encoded
// cursors a later request parses back; and the spreadsheet exports a ministry reads. An unpinned
// process renders a score of seven and a half with a comma on a host whose locale says so, and nothing
// here would notice.
//
// This repository has been burned by the ambient locale twice, and both times the fix was one call
// site. This is the whole class of problem closed once, so a new formatting site added tomorrow is
// correct by default rather than by whoever remembers. The call sites found at the time are explicit as
// well, so they stay correct even on a thread that sets its own locale.
//
// Nothing user-facing is lost. The API speaks a format that is locale-independent by specification, and
// every number and date a person reads is formatted by the interface in the reader's own language.
//
//
// LOGGING
//
// Structured records to the console. The redacting stage must stay registered before the output, so no
// value on the deny list can reach the console or an aggregator.
//
//
// TRACES AND METRICS
//
// Traces already existed. Metrics did not: there was no instrumentation anywhere in the backend.
//
// The framework's own built-in meters give request count, duration and status per route, for every
// route, at no cost. That is strictly more complete than the hand-picked list of high-traffic endpoints
// the requirement offered as a fallback.
//
// They are named as the framework's own meters rather than pulled in as a separate instrumentation
// package. Unlike tracing, the framework emits its metrics natively, and its instrumentation package
// only wires up tracing, which was confirmed by trying it and getting a compile error. The identity
// meter is the other free one worth having, for sign-in and password-check counts on an application
// whose whole surface sits behind signing in.
//
// The application's own meter covers the small amount that instrumentation cannot see.
//
// The exporter publishes metrics for something to come and collect. It is the only one of its kind for
// this platform and it carries a pre-release version, matching the other packages in the same family,
// which have all stayed pre-release upstream for a long time despite wide production use. Evaluated and
// accepted rather than used without looking.
//
//
// THE TWO HEALTH PROBES
//
// The split was specified before any of it existed, and it exists to avoid one particular failure: an
// orchestrator restarting a perfectly healthy process because a dependency it does not own was briefly
// down, or sending traffic to a copy that said it was alive while unable to serve anything.
//
// Readiness means the database is reachable, the migrations are applied, the object store is reachable
// and the job storage is reachable. Liveness means the process is responsive, and it checks no
// dependency at all.
//
// The finance system is deliberately not a readiness gate, because the portal works without it, and
// that integration is not built yet in any case.
//
// Every readiness check is tagged as such, and nothing is tagged for liveness, so liveness runs zero
// checks by design.

namespace MotsSupplierPortal.Api.Startup;

using Hangfire;
using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

internal static class ObservabilityRegistration
{
    internal static void PinInvariantCulture()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
    }

    internal static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With(new MotsSupplierPortal.Infrastructure.Observability.RedactingEnricher())
            .Enrich.WithProperty("Application", "MotsSupplierPortal.Api")
            .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

        builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Observability.AppMetrics>();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("MotsSupplierPortal.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.Identity")
                .AddMeter(MotsSupplierPortal.Infrastructure.Observability.AppMetrics.MeterName)
                .AddPrometheusExporter());

        return builder;
    }

    internal static WebApplicationBuilder AddHealthProbes(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddSingleton(_ => JobStorage.Current);

        builder.Services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
            .AddCheck<MotsSupplierPortal.Infrastructure.Observability.MigrationsAppliedHealthCheck>(
                "migrations", tags: ["ready"])
            .AddCheck<MotsSupplierPortal.Infrastructure.Observability.ObjectStorageHealthCheck>(
                "object-storage", tags: ["ready"])
            .AddCheck<MotsSupplierPortal.Infrastructure.Observability.HangfireStorageHealthCheck>(
                "hangfire-storage", tags: ["ready"]);

        return builder;
    }
}

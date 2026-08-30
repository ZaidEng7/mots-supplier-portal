using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #16/NFR-OBS-006: /metrics is a real Prometheus scrape target, not just a mapped route that
/// returns 200. Proven by triggering a real rate-limit rejection over HTTP and confirming the
/// custom counter shows up in the scraped output with its tags - not merely that ASP.NET Core's
/// own built-in request metrics appear (which would be true even if AppMetrics were never wired
/// into the rate limiter at all).
///
/// <para><b>Each test derives its own WebApplicationFactory</b> (fixture.WithWebHostBuilder with no
/// overrides) rather than using the shared fixture's client directly. Found by hitting it: other
/// test files in this suite (RegistrationRateLimitTests, StreamingUploadTests) also derive their
/// own hosts for their own reasons, and each derived host builds its own OpenTelemetry
/// MeterProvider + Prometheus exporter. Multiple of those coexisting in one test process is enough
/// to make the SHARED fixture's /metrics scrape intermittently come back with only the boilerplate
/// target_info line and none of the app's own instruments - reproduced directly (passed reliably
/// alone, failed intermittently as part of the full suite). An isolated host sidesteps whatever
/// that cross-provider interaction is rather than depending on test execution order to avoid it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MetricsEndpointTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Metrics_endpoint_reports_the_built_in_ASPNET_Core_request_histogram()
    {
        await using var factory = fixture.WithWebHostBuilder(_ => { });
        var client = factory.CreateClient();

        // Any request first, so http.server.request.duration has at least one recorded sample -
        // Prometheus exporters typically omit an instrument entirely until it has a measurement.
        await client.GetAsync("/health/live");

        var response = await client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("http_server_request_duration",
            "the built-in ASP.NET Core meter (Microsoft.AspNetCore.Hosting) must actually be wired " +
            "into the exporter, not just referenced in a comment");
    }

    [Fact]
    public async Task A_real_rate_limit_rejection_over_HTTP_shows_up_in_the_scraped_output()
    {
        await using var factory = fixture.WithWebHostBuilder(_ => { });
        var client = factory.CreateClient();
        var email = $"metrics-probe-{Guid.NewGuid():N}@example.com";

        // register-strict is 5/min per-target (task #4/NFR-SEC-009) - the 6th over-budget attempt
        // is a genuine 429, not a simulated one.
        for (var i = 0; i < 6; i++)
        {
            await client.PostAsJsonAsync("/api/v1/registrations", new
            {
                displayNameAr = "شركة",
                displayNameEn = $"Metrics Probe {i}",
                registrationNumber = (string?)null,
                representativeName = "Probe",
                representativePhone = "+963900000000",
                email,
                password = "MetricsProbe#2026!",
            });
        }

        // Poll rather than a single read: the OTel SDK's metric collection cycle can lag a
        // just-recorded measurement by a beat, the same latency any real Prometheus scrape has -
        // this simulates "will a scrape eventually see it", not papering over a broken feature.
        var body = "";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            body = await (await client.GetAsync("/metrics")).Content.ReadAsStringAsync();
            if (body.Contains("mots_rate_limit_rejections_total")) break;
            await Task.Delay(100);
        }

        body.Should().Contain("mots_rate_limit_rejections_total",
            "the custom counter must reach the exporter, not just exist in the Meter");
        body.Should().Contain("surface=\"register\"");
    }
}

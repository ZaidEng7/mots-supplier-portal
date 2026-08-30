using System.Net;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #16/NFR-OBS-006: liveness and readiness are genuinely different endpoints answering
/// genuinely different questions, not the same handler under two names
/// (docs/architecture/OBSERVABILITY-ARCHITECTURE.md §5).
///
/// <para><b>Why this doesn't break a real dependency live and watch the two endpoints diverge.</b>
/// That was the first attempt: point Minio:Endpoint at an unreachable address on a
/// fixture.WithWebHostBuilder-derived host and compare /health/live vs /health/ready. It doesn't
/// work, and not because the split is wrong - Program.cs's OWN STARTUP calls
/// MinioFileStorage.EnsureBucketExistsAsync before the host finishes building, so an unreachable
/// object store crashes host startup entirely, not just the readiness check. There is no way to
/// get a running host with object storage broken but everything else fine through this app's own
/// boot sequence, which is itself a correct property (the app should not start if it cannot reach
/// its own storage) - it just is not a usable test fixture for isolating readiness from liveness.
/// Proof is split instead: the READINESS half is proven per-check in
/// Tests/Unit/Observability (ObjectStorageHealthCheckTests, HangfireStorageHealthCheckTests -
/// each reports Unhealthy against a REAL failure, not a mock configured to lie), and the LIVENESS
/// half is structural - Predicate = _ => false means zero IHealthCheck ever runs for that route,
/// which is verified directly below by confirming the response contains no check names at all
/// (nothing to fail, by construction) while readiness's response does.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class HealthCheckSplitTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Liveness_and_readiness_both_pass_when_every_dependency_is_healthy()
    {
        var client = fixture.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Liveness_runs_no_dependency_checks_at_all_while_readiness_runs_every_documented_one()
    {
        // Denominator, both directions. Readiness: docs/architecture/OBSERVABILITY-ARCHITECTURE.md
        // §5 names exactly 4 checks (Postgres connectivity, migrations applied, object storage,
        // Hangfire storage) - asserted by name so one silently dropped from AddHealthChecks (the
        // same "instrument reporting over an empty set" pathology this arc keeps finding, MSP-83)
        // fails here rather than reading as an unremarkable 200. Liveness: the response must name
        // NO checks at all - not "0 unhealthy", an actual empty check list, proving Predicate =
        // _ => false is doing what it claims rather than merely returning fast for other reasons.
        var client = fixture.CreateClient();

        var readyBody = await (await client.GetAsync("/health/ready")).Content.ReadAsStringAsync();
        readyBody.Should().Contain("postgres").And.Contain("migrations")
            .And.Contain("object-storage").And.Contain("hangfire-storage");

        var liveBody = await (await client.GetAsync("/health/live")).Content.ReadAsStringAsync();
        liveBody.Should().NotContain("postgres").And.NotContain("migrations")
            .And.NotContain("object-storage").And.NotContain("hangfire-storage");
    }
}

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Spins up real PostgreSQL and MinIO containers (Testcontainers) and boots the actual API host
/// (WebApplicationFactory&lt;Program&gt;) against them, applying the real EF Core migrations -
/// no mocked persistence, matching docs/backlog gap item 3's "Testcontainers-backed
/// integration tests" requirement. MinIO is required because Program.cs calls
/// EnsureBucketExistsAsync at startup (MSP-49's document storage) - without a real endpoint the
/// whole host fails to boot, failing every test in this fixture, not just document ones.
/// </summary>
public sealed class PostgresApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // CS0618: the parameterless PostgreSqlBuilder is obsolete and the image must now be explicit.
    // Pinned to a specific major rather than `latest` on purpose - these tests assert behaviour
    // that belongs to Postgres itself (xmin row versioning, ON CONFLICT allocation), so an
    // unpinned image would let a silent upstream bump change what the suite is testing.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:latest").Build();

    // MSP-84/NFR-PERF-008: no official Testcontainers.ClamAv module exists, so this is the generic
    // ContainerBuilder against the same image docker-compose.yml already uses for local dev. Real
    // clamd, not a stub - the streaming-upload fix's whole point is proving the AV-scan path still
    // rejects malware correctly once it no longer buffers the file first, and a stubbed scanner
    // couldn't prove that. Startup is slow (clamd loads virus definitions on boot, docker-compose's
    // own healthcheck allows up to 180s) - paid once per test run via IntegrationTestCollection's
    // single shared fixture, not once per test.
    private readonly IContainer _clamav = new ContainerBuilder("clamav/clamav:stable")
        .WithPortBinding(3310, true)
        // Mirrors docker-compose.yml's own clamav healthcheck exactly (PING/PONG over the
        // INSTREAM port), not just "the port accepted a TCP connection" - clamd can open the
        // port before virus definitions finish loading, which would make an EICAR scan below
        // fail closed for the wrong reason (definitions not ready) rather than proving anything.
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilCommandIsCompleted("sh", "-c", "echo PING | nc -w 3 localhost 3310 | grep -q PONG"))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync(), _clamav.StartAsync());

        // Migrate with a standalone DbContext BEFORE the host boots: Program.cs seeds Identity
        // roles as part of startup (Development-only), which needs the identity schema to
        // already exist - touching Services here would start the host first and seed against
        // an empty database.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // Now touch Services to actually boot the host against the migrated schema.
        _ = Services;
    }

    /// <summary>
    /// Every client in the suite carries <see cref="ETagAttachingHandler"/>, so §8.1's If-Match
    /// requirement does not have to be repeated in each of the ~300 assertions written before it.
    /// The concurrency tests construct their own client without it - a handler that always sends a
    /// current version cannot observe a stale one.
    /// </summary>
    /// `new` rather than `override` because WebApplicationFactory.CreateClient is not virtual.
    /// Every call site in the suite has the fixture as its static type, so they all bind to this one.
    public new HttpClient CreateClient() =>
        CreateDefaultClient(new ETagAttachingHandler());

    /// <summary>A client WITHOUT the ETag handler, for tests that need to control the header.</summary>
    public HttpClient CreateRawClient() => ((WebApplicationFactory<Program>)this).CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // MSP-98: no recurring job may fire under the suite. Hangfire itself stays on - tests
        // invoke jobs directly (AwardEndpointsTests runs AwardErpSyncJob against a deliberately
        // failing adapter, which IS the behaviour under test) and enqueued email jobs still
        // process. What is switched off is the SCHEDULER: a job runs when a test asks for it, and
        // never when a test does not.
        //
        // Without this, award-erp-sync (*/5) synced an award a test had staged to fail, once the
        // suite grew long enough to span a tick. That failure was loud. The one that is not loud is
        // a test asserting a state a job also produces - SubmissionClosed, Expired - and passing
        // because the job did the work.
        builder.UseSetting("Jobs:EnableRecurring", "false");

        var minioEndpoint = new Uri(_minio.GetConnectionString());
        builder.UseSetting("Minio:Endpoint", $"{minioEndpoint.Host}:{minioEndpoint.Port}");
        builder.UseSetting("Minio:AccessKey", _minio.GetAccessKey());
        builder.UseSetting("Minio:SecretKey", _minio.GetSecretKey());
        builder.UseSetting("Minio:UseSsl", "false");

        builder.UseSetting("ClamAv:Host", _clamav.Hostname);
        builder.UseSetting("ClamAv:Port", _clamav.GetMappedPublicPort(3310).ToString());

        // Keep CI hermetic: the HIBP breach-password check (HibpBreachedPasswordValidator) calls
        // an external API - fine to fail open in prod/dev, but a flaky/offline network shouldn't
        // ever be why an integration test fails.
        builder.UseSetting("Password:BreachCheckEnabled", "false");

        // Every test class shares this one host (IntegrationTestCollection), so they also share the
        // per-IP auth rate-limit partition. At the production default of 10/min the suite throttles
        // itself and the resulting empty 429 bodies present as JSON parse errors far from the cause.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "10000");
        // Same reasoning, same fix, for the registration-specific per-IP policy (NFR-SEC-009).
        builder.UseSetting("RateLimiting:RegisterPermitLimit", "10000");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask(), _clamav.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}

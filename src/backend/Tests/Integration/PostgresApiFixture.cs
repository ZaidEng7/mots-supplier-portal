// The fixture the whole integration suite shares: real containers, the real host, the real migrations.
//
// It starts PostgreSQL, object storage and the virus scanner as containers, boots the actual API against them,
// and applies the real migrations. No mocked persistence.
//
// Object storage is required rather than optional, because the host ensures its bucket exists at startup, so
// without a real endpoint the whole host fails to boot and every test in the fixture fails with it, not only the
// document ones.
//
// The scanner is real too. There is no ready-made module for it, so it is the generic container against the same
// image the local stack uses. The streaming-upload work's whole point was proving the scan path still rejects
// malware once it no longer buffers the file first, and a stubbed scanner could not prove that. Its startup is
// slow, because the daemon loads virus definitions on boot, and that is paid once per run through the single
// shared fixture rather than once per test.
//
// Its readiness probe mirrors the local stack's own: a real handshake over the scanning port, not merely "the
// port accepted a connection". The daemon can open the port before definitions finish loading, which would make
// a malware sample fail closed for the wrong reason rather than proving anything.
//
//
// EVERY IMAGE IS PINNED, AND ONE OF THEM WAS LEARNED THE EXPENSIVE WAY
//
// The database is pinned because these tests assert behaviour belonging to the database itself, so an unpinned
// image would let a silent upstream bump change what the suite is testing.
//
// The object store used to float on its latest tag. Then the registry it was pulled from stopped serving that
// repository, because the vendor publishes elsewhere. Nothing in this repository changed. Every backend job went
// red at once, one millisecond into the run, with a pull-access error, and it was invisible on any developer
// machine that had already pulled the image, because the tooling reuses what it has.
//
// A floating tag is what made a decision somebody else took land in this repository as a failure. A pinned
// release changes only when a person changes it.
//
//
// THE ORDER OF MIGRATION AND BOOT
//
// The migrations run through a standalone context BEFORE the host boots, because the host seeds identity roles as
// part of startup and that needs the identity schema to already exist. Touching the host's services first would
// boot it and seed against an empty database.
//
//
// WHAT IS SWITCHED OFF UNDER THE SUITE, AND WHY EACH ONE
//
// The demonstration data seeder. It exists so no screen renders empty in a manual walkthrough, and its suppliers
// appeared in another suite's page-one assertions, which name the rows they expect. A fixture that plants rows
// tests do not know about makes every count and every ordering assertion conditional on it, and only in a full
// run.
//
// The recurring job SCHEDULER, but not the job framework. Tests invoke jobs directly, which is the behaviour
// under test in places, and enqueued email jobs still process. A job runs when a test asks for it and never when
// a test does not.
//
// Without that, a scheduled synchronisation job synced an award a test had staged to fail, once the suite grew
// long enough to span a tick. That failure was loud. The one that is not loud is a test asserting a state a job
// also produces, and passing because the job did the work.
//
// The external breach-password lookup, to keep continuous integration hermetic. Failing open is fine in
// production; a flaky or offline network should never be why an integration test fails.
//
// And both per-address rate limits are raised, because every test class shares this one host and therefore one
// limiter partition. At the production defaults the suite throttles itself, and the resulting empty refusals
// present as parse errors far from the cause.
//
//
// THE CLIENTS THIS FIXTURE HANDS OUT
//
// The default one carries the version-header handler, so the precondition requirement does not have to be
// repeated in the three hundred assertions written before it existed. The concurrency tests construct their own
// without it, because a handler that always sends a current version cannot observe a stale one.
//
// It shadows the factory's own method rather than overriding it, because that method is not virtual. Every call
// site has the fixture as its static type, so they all bind to this one.
//
// There is also a client that does not follow redirects, so a documented redirect can be asserted as one rather
// than as whatever the signed link answers.
//
//
// THE SHARED-ROW LEAK CHECK
//
// The state of the globally shared rows is snapshotted before any test runs and compared again when the run ends.
//
// It runs BEFORE the containers go away, because it needs the database. A failure is thrown rather than asserted,
// because this is a fixture and the runner surfaces that as a run-level error naming the collection, which is
// the right shape: the leak belongs to the RUN rather than to whichever test happened to be last.
//
// A database that has already gone away is not a leak and must not be reported as one, because a false failure
// here would land on whatever test ran last and cost somebody an hour.
//
// The comparison is also public, so a test can ask for it. The end-of-run check reports at collection level, and
// the runner's adapter exits successfully on a collection cleanup failure: loud in the log, invisible to
// continuous integration. A dedicated test is what makes it a gate.

namespace MotsSupplierPortal.Tests.Integration;

using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

public sealed class PostgresApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly MinioContainer _minio =
        new MinioBuilder("quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z").Build();

    private readonly IContainer _clamav = new ContainerBuilder("clamav/clamav:stable")
        .WithPortBinding(3310, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilCommandIsCompleted("sh", "-c", "echo PING | nc -w 3 localhost 3310 | grep -q PONG"))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync(), _clamav.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _ = Services;

        await using var snapshotScope = Services.CreateAsyncScope();
        _seededGlobals = await GlobalRowSnapshot.TakeAsync(
            snapshotScope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private IReadOnlyDictionary<string, string>? _seededGlobals;

    public new HttpClient CreateClient() =>
        CreateDefaultClient(new ETagAttachingHandler());

    public HttpClient CreateRawClient() => ((WebApplicationFactory<Program>)this).CreateClient();

    public HttpClient CreateClientWithoutRedirects() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        builder.UseSetting("DevSeed:Enabled", "false");

        builder.UseSetting("Jobs:EnableRecurring", "false");

        var minioEndpoint = new Uri(_minio.GetConnectionString());
        builder.UseSetting("Minio:Endpoint", $"{minioEndpoint.Host}:{minioEndpoint.Port}");
        builder.UseSetting("Minio:AccessKey", _minio.GetAccessKey());
        builder.UseSetting("Minio:SecretKey", _minio.GetSecretKey());
        builder.UseSetting("Minio:UseSsl", "false");

        builder.UseSetting("ClamAv:Host", _clamav.Hostname);
        builder.UseSetting("ClamAv:Port", _clamav.GetMappedPublicPort(3310).ToString());

        builder.UseSetting("Password:BreachCheckEnabled", "false");

        builder.UseSetting("RateLimiting:AuthPermitLimit", "10000");
        builder.UseSetting("RateLimiting:RegisterPermitLimit", "10000");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        var drift = await DriftAsync();

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask(), _clamav.DisposeAsync().AsTask());
        await base.DisposeAsync();

        if (drift is not null) throw new InvalidOperationException(drift);
    }

    public Task<string?> GlobalRowDriftAsync() => DriftAsync();

    private async Task<string?> DriftAsync()
    {
        if (_seededGlobals is null) return null;

        try
        {
            await using var scope = Services.CreateAsyncScope();
            var after = await GlobalRowSnapshot.TakeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            return GlobalRowSnapshot.Drift(_seededGlobals, after);
        }
        catch (Exception ex)
        {
            return $"The global-row check could not read the database at the end of the run: {ex.Message}";
        }
    }
}

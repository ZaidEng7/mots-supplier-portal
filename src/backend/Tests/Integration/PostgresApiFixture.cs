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

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        var minioEndpoint = new Uri(_minio.GetConnectionString());
        builder.UseSetting("Minio:Endpoint", $"{minioEndpoint.Host}:{minioEndpoint.Port}");
        builder.UseSetting("Minio:AccessKey", _minio.GetAccessKey());
        builder.UseSetting("Minio:SecretKey", _minio.GetSecretKey());
        builder.UseSetting("Minio:UseSsl", "false");

        // Keep CI hermetic: the HIBP breach-password check (HibpBreachedPasswordValidator) calls
        // an external API - fine to fail open in prod/dev, but a flaky/offline network shouldn't
        // ever be why an integration test fails.
        builder.UseSetting("Password:BreachCheckEnabled", "false");

        // Every test class shares this one host (IntegrationTestCollection), so they also share the
        // per-IP auth rate-limit partition. At the production default of 10/min the suite throttles
        // itself and the resulting empty 429 bodies present as JSON parse errors far from the cause.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "10000");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}

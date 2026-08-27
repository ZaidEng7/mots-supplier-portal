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
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
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
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}

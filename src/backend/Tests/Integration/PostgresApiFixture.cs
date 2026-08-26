using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Spins up a real PostgreSQL container (Testcontainers) and boots the actual API host
/// (WebApplicationFactory&lt;Program&gt;) against it, applying the real EF Core migrations -
/// no mocked persistence, matching docs/backlog gap item 3's "Testcontainers-backed
/// integration tests" requirement.
/// </summary>
public sealed class PostgresApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

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
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

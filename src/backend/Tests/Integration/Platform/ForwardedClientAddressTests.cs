// The per-address rate limit has to count actual callers, and behind a proxy it did not.
//
// WHAT WENT WRONG
//
// Connection.RemoteIpAddress is the socket peer. With a reverse proxy in front, and the deployment
// architecture describes one, that is the proxy on every request - so ten requests a minute was one
// bucket for the whole internet, and the address stamped on every audit row was the proxy's.
//
// THE CONTROL IS THE POINT OF THIS FILE, not the fix. A limiter that partitions on the wrong value
// still returns 429 at the right count, still emits metrics, and still passes any test that only
// asks whether it refuses. It looks exactly like a working limiter. So the first case proves that a
// spoofed header from an UNTRUSTED connection is ignored - which is the behaviour before the fix,
// asserted deliberately - and the second proves the header is honoured from a trusted one. Either
// case alone can be satisfied by a broken implementation: trusting nothing passes the first,
// trusting everything passes the second. Only the pair pins the behaviour.
//
// The suite's own hosts are no use here. They raise the permit limit to ten thousand so the shared
// partition does not throttle the suite itself, so nothing there can ever reach a refusal. These
// hosts set it low on purpose. They also need a real database, even though the interesting requests
// are refused in middleware before any endpoint runs: the requests that are NOT refused go on to the
// endpoint, and pointing them at a database that is not there turns "the limiter allowed it" into a
// connection error. The container is what keeps an allowed request distinguishable from a broken one.
//
// Object storage is real for the same reason. Startup contacts the bucket unconditionally, so a host
// without it never finishes starting. The first version of this file left it out and passed locally
// against the developer's running stack while failing on a CI runner that had none - a test that
// passes because of something outside it is the defect this repository keeps writing tests about.
//
// Hangfire prepares its own schema on these hosts. Jobs:EnableRecurring=false stops THIS application
// from scheduling work, but the Hangfire server still starts and still takes its distributed locks,
// and with no hangfire schema to take them in every host start times out.
//
// TestServer leaves RemoteIpAddress null, so each host installs a first-in-line middleware that
// stands in for the socket peer. It runs ahead of the forwarded-headers middleware because a startup
// filter wraps the application's own pipeline, which is the ordering the real defect turned on.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Infrastructure.Persistence;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

public sealed class ForwardedClientAddressTests : IAsyncLifetime
{
    private const int PermitLimit = 3;

    private const string Proxy = "10.42.0.9";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    // The image name comes from the shared fixture rather than being written again here. This class needs its
    // own host, so it builds its own containers - but a second copy of the image reference is a second thing to
    // find when the image moves, and when MinIO withdrew its public images this file was the one that was
    // missed: the suite went green everywhere except these two tests, which failed on a pull nobody was looking
    // at any more.
    private readonly MinioContainer _minio = new MinioBuilder(PostgresApiFixture.MinioImage).Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() =>
        Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_connection_is_ignored()
    {
        using var host = NewHost(trustTheProxy: false);

        var statuses = await AttemptAsync(host, PermitLimit + 1);

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "nothing vouches for the header on an untrusted connection, so every request shares the "
            + "socket address's bucket and the limit is reached - a caller must not be able to buy "
            + "itself a fresh allowance by editing a header");
    }

    [Fact]
    public async Task A_forwarded_address_from_the_proxy_partitions_the_limit()
    {
        using var host = NewHost(trustTheProxy: true);

        var statuses = await AttemptAsync(host, PermitLimit + 1);

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests,
            "each request names a different client, so each gets its own bucket - otherwise one "
            + "attacker spends the allowance for everybody behind the proxy");
    }

    private ProxyAwareHost NewHost(bool trustTheProxy)
    {
        var storage = new Uri(_minio.GetConnectionString());
        return new ProxyAwareHost(
            _postgres.GetConnectionString(),
            $"{storage.Host}:{storage.Port}",
            _minio.GetAccessKey(),
            _minio.GetSecretKey(),
            trustTheProxy);
    }

    private static async Task<List<HttpStatusCode>> AttemptAsync(ProxyAwareHost host, int attempts)
    {
        using var client = host.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
            {
                Content = JsonContent.Create(new { email = $"probe{attempt}@example.test" }),
            };
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt}");

            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private sealed class ProxyAwareHost(
        string connectionString,
        string storageEndpoint,
        string storageAccessKey,
        string storageSecretKey,
        bool trustTheProxy) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Minio:Endpoint", storageEndpoint);
            builder.UseSetting("Minio:AccessKey", storageAccessKey);
            builder.UseSetting("Minio:SecretKey", storageSecretKey);
            builder.UseSetting("Minio:UseSsl", "false");
            builder.UseSetting("DevSeed:Enabled", "false");
            builder.UseSetting("Jobs:EnableRecurring", "false");
            builder.UseSetting("Password:BreachCheckEnabled", "false");
            builder.UseSetting("RateLimiting:AuthPermitLimit", PermitLimit.ToString());

            if (trustTheProxy)
            {
                builder.UseSetting("Network:TrustedProxies:0", Proxy);
            }

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new SocketPeerIs(Proxy)));
        }
    }

    private sealed class SocketPeerIs(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, proceed) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                    await proceed();
                });
                next(app);
            };
    }
}

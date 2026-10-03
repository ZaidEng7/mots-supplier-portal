// The on-demand probe of the object store and the virus scanner, POST /api/v1/admin/dashboard/storage-probe, and
// the promise that nothing else on the dashboard or the operations page asks the scanner.
//
// The fixture runs a real object store and a real scanner, so the healthy answer is the real one. The failing
// cases replace one dependency at a time on a derived host: a scanner that says it could not scan, one that
// throws, and, for the cap, a scanner and an object-store check that never answer and ignore their cancellation.
// The cap is proved by how long the answer takes, because a probe that honoured the dependency's own timeout
// would take three minutes for the scanner.
//
// The storage settings and the dashboard are read on a host whose scanner and object-store check count their
// calls. Opening the operations page used to scan an empty stream and ping the store every time; now the settings
// call neither, and the dashboard pings the store once and never scans.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class DashboardStorageProbeTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard/storage-probe";

    private const string ObjectStorageCheck = "object-storage";

    [Fact]
    public async Task An_administrator_is_told_both_answer_and_when_they_were_asked()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync(Route, null);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        var probe = JsonDocument.Parse(text).RootElement;
        probe.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["objectStorageReachable", "virusScannerReachable", "checkedAt"],
                "a yes or no for each, and when; no host name or exception text");
        probe.GetProperty("objectStorageReachable").GetBoolean().Should().BeTrue();
        probe.GetProperty("virusScannerReachable").GetBoolean().Should().BeTrue();
        probe.GetProperty("checkedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Nobody_without_admin_users_manage_can_make_it_ask()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Probe Outsider Co");
        (await supplier.PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await fixture.CreateRawClient().PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_scanner_that_could_not_scan_or_threw_is_unreachable_and_says_nothing_more()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await using (var unavailable = HostWith(services =>
                         services.AddSingleton<IVirusScanner>(new FixedScanner(ScanOutcome.Unavailable))))
        {
            var probe = await ProbeAsync(SignedInOn(unavailable, admin));
            probe.GetProperty("virusScannerReachable").GetBoolean().Should().BeFalse(
                "a scanner that is down reports the scan unavailable rather than throwing");
            probe.GetProperty("objectStorageReachable").GetBoolean().Should().BeTrue();
        }

        await using (var infected = HostWith(services =>
                         services.AddSingleton<IVirusScanner>(new FixedScanner(ScanOutcome.Infected))))
        {
            (await ProbeAsync(SignedInOn(infected, admin))).GetProperty("virusScannerReachable").GetBoolean()
                .Should().BeFalse("a working scanner cannot call an empty stream infected");
        }

        const string secret = "clamd-internal-host-detail";
        await using var throwing = HostWith(services => services.AddSingleton<IVirusScanner>(new ThrowingScanner(secret)));
        var response = await SignedInOn(throwing, admin).PostAsync(Route, null);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        JsonDocument.Parse(text).RootElement.GetProperty("virusScannerReachable").GetBoolean().Should().BeFalse();
        text.Should().NotContain(secret, "the exception stays on the server");
    }

    [Fact]
    public async Task Dependencies_that_never_answer_cost_the_probe_ten_seconds_and_no_more()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await using var host = HostWith(services =>
        {
            services.AddSingleton<IVirusScanner, HangingScanner>();
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var existing = options.Registrations.Single(r => r.Name == ObjectStorageCheck);
                options.Registrations.Remove(existing);
                options.Registrations.Add(new HealthCheckRegistration(ObjectStorageCheck, new HangingCheck(), null, existing.Tags));
            });
        });
        var client = SignedInOn(host, admin);

        var clock = Stopwatch.StartNew();
        var probe = await ProbeAsync(client);
        clock.Stop();

        probe.GetProperty("objectStorageReachable").GetBoolean().Should().BeFalse();
        probe.GetProperty("virusScannerReachable").GetBoolean().Should().BeFalse();
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(9.5), "each is given its ten seconds");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(14),
            "the two are asked side by side and each is abandoned at ten seconds, whatever it does with its cancellation");
    }

    [Fact]
    public async Task Opening_the_storage_settings_or_the_dashboard_never_scans_and_only_the_dashboard_pings_the_store()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var scanner = new FixedScanner(ScanOutcome.Clean);
        var check = new CountingCheck();
        await using var host = HostWith(services =>
        {
            services.AddSingleton<IVirusScanner>(scanner);
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var existing = options.Registrations.Single(r => r.Name == ObjectStorageCheck);
                options.Registrations.Remove(existing);
                options.Registrations.Add(new HealthCheckRegistration(ObjectStorageCheck, check, null, existing.Tags));
            });
        });
        var client = SignedInOn(host, admin);

        var settings = await client.GetAsync("/api/v1/admin/storage");
        var text = await settings.Content.ReadAsStringAsync();
        settings.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var body = JsonDocument.Parse(text).RootElement;
        body.TryGetProperty("objectStorageReachable", out _).Should().BeFalse("reachability is the probe's now");
        body.TryGetProperty("virusScannerReachable", out _).Should().BeFalse();

        scanner.Calls.Should().Be(0, "opening the operations page must not scan anything");
        check.Calls.Should().Be(0, "opening the operations page must not ping the object store");

        (await client.GetAsync("/api/v1/admin/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
        scanner.Calls.Should().Be(0, "the dashboard refreshes every minute and does not scan");
        check.Calls.Should().Be(1, "the dashboard pings the object store once");

        await ProbeAsync(client);
        scanner.Calls.Should().Be(1, "the probe is the one thing that scans");
        check.Calls.Should().Be(2);
    }

    private static async Task<JsonElement> ProbeAsync(HttpClient client)
    {
        var response = await client.PostAsync(Route, null);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private WebApplicationFactory<Program> HostWith(Action<IServiceCollection> overrides)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            overrides(services);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
        }));
    }

    private static HttpClient SignedInOn(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    private sealed class FixedScanner(ScanOutcome outcome) : IVirusScanner
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(outcome);
        }
    }

    private sealed class ThrowingScanner(string message) : IVirusScanner
    {
        public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct) =>
            throw new InvalidOperationException(message);
    }

    // Never answers, and does not listen to its cancellation either.
    private sealed class HangingScanner : IVirusScanner
    {
        public async Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(40), CancellationToken.None);
            return ScanOutcome.Clean;
        }
    }

    private sealed class HangingCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(40), CancellationToken.None);
            return HealthCheckResult.Healthy();
        }
    }

    private sealed class CountingCheck : IHealthCheck
    {
        private int _calls;

        public int Calls => _calls;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}

// The frame of the administrator's dashboard: who may read it, which sections a viewer sees, what a failing
// section turns into, and that the sections really run side by side in scopes of their own.
//
// The figures inside each section are tested beside the section that computes them, in this folder, one class per
// section. This file is about the frame, so most of it runs on a host whose sections are replaced by test
// sections that record what happened to them. The real sections cannot be made to throw on demand, and a section
// that is hidden can only be shown never to have run by a section that would have said so.
//
// The test sections return a record made without calling its constructor. The shape of each section's data
// belongs to that section, and this file must not need editing every time a section gains a field.
//
// One test runs on the fixture's own host with the real sections, so a section that is not registered, or that
// cannot be resolved from a scope of its own, fails here before anybody builds a screen on it.
//
//
// THE GATES ARE TESTED BOTH WAYS
//
// The route refuses every persona that lacks admin.users.manage, and also a viewer granted every permission the
// sections use except that one. Without the second case a route gated on audit.read by mistake would refuse the
// same seeded personas and pass.
//
// Inside the route, the three section gates are tested over every combination of audit.read and
// admin.integrations.manage, on a viewer who holds admin.users.manage and nothing else of note. Two gates
// swapped with each other, or one dropped, change at least one row of that table.
//
// The viewer is a ministry viewer granted the permissions for the duration of one test, because no seeded role
// holds admin.users.manage without the rest. The grant is a read of the role's whole set first and a write of
// that same set back in a finally, as the roles tests explain. The two section permissions are taken out of the
// set before the grant rather than assumed absent, so the table means what it says even if the seeded role
// changes.
//
//
// A FAILED SECTION SAYS NOTHING ABOUT WHY
//
// The thrown message carries a marker that must not appear anywhere in the answer, the section carries exactly a
// status and no data, and the failure is asserted to have been logged with the section's name, because a
// section that failed silently would be found only by somebody who already suspected it.
//
// The log is read by replacing the dashboard handler's own logger. The host writes through Serilog, which takes
// over the logger factory and ignores any logger provider a test adds, so a recorder added that way would see
// nothing and the assertion would fail for the wrong reason.
//
//
// RUNNING SIDE BY SIDE IS PROVED, NOT TIMED
//
// Each of the five test sections blocks synchronously until all five have started, which no sequential run and
// no run that lets one section's synchronous work hold up the next can get past. Then all five use their own
// database context at the same moment. Run from one shared scope they would share one context, and the
// framework refuses a second operation on a context that is still busy.
//
//
// WHAT IS LEFT BEHIND
//
// Every derived host is disposed at the end of its test, and the ministry viewer's permissions are put back. The
// accounts these tests sign in with stay, each under its own unique address, like every other class's, and so do
// the audit rows the role edits wrote, because the audit table is append-only.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Admin.Dashboard;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class AdminDashboardFrameTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    private static readonly string[] Sections =
        ["systemHealth", "erp", "peopleAndAccess", "security", "recentActivity", "needsAttention"];

    private static readonly Dictionary<Type, string> WireName = new()
    {
        [typeof(DashboardSystemHealthDto)] = "systemHealth",
        [typeof(DashboardErpDto)] = "erp",
        [typeof(DashboardPeopleAndAccessDto)] = "peopleAndAccess",
        [typeof(DashboardSecurityDto)] = "security",
        [typeof(DashboardRecentActivityDto)] = "recentActivity",
        [typeof(DashboardNeedsAttentionDto)] = "needsAttention",
    };

    [Fact]
    public async Task Nobody_without_admin_users_manage_can_read_it()
    {
        foreach (var role in new[]
                 {
                     Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer,
                     Roles.OnboardingReviewer, Roles.Evaluator,
                 })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync(Route)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold admin.users.manage");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Dashboard Outsider Co");
        (await supplier.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await fixture.CreateRawClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        (await admin.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Holding_every_section_permission_except_admin_users_manage_is_still_refused()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var original = await PermissionsOfAsync(admin, Roles.MinistryViewer);

        try
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer,
                [
                    .. original.Where(p => p != Permissions.AdminUsersManage),
                    Permissions.AuditRead, Permissions.AdminIntegrationsManage, Permissions.SupplierImportRun,
                ]);

            var viewer = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

            (await viewer.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "the route is gated on admin.users.manage, and the permissions that open single sections do not open it");
        }
        finally
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer, original);
        }
    }

    [Fact]
    public async Task An_administrator_gets_all_six_real_sections_ok_with_their_data()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var section in Sections)
        {
            var result = body.GetProperty(section);
            result.GetProperty("status").GetString().Should().Be("ok", $"{section} should have run for the administrator");
            result.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Object, $"an ok {section} carries its data");
        }

        body.GetProperty("generatedAt").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_section_the_viewer_may_not_see_is_hidden_and_never_runs(bool auditRead, bool integrations)
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var original = await PermissionsOfAsync(admin, Roles.MinistryViewer);
        var granted = original
            .Where(p => p != Permissions.AuditRead && p != Permissions.AdminIntegrationsManage)
            .Append(Permissions.AdminUsersManage)
            .Concat(auditRead ? [Permissions.AuditRead] : Array.Empty<string>())
            .Concat(integrations ? [Permissions.AdminIntegrationsManage] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        try
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer, granted);
            var (viewer, viewerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.MinistryViewer);

            var probe = new Probe();
            await using var host = HostWith(probe, _ => { });

            var body = await DashboardAsync(host, viewer);

            var expected = new Dictionary<string, string>
            {
                ["systemHealth"] = "ok",
                ["erp"] = integrations ? "ok" : "hidden",
                ["peopleAndAccess"] = "ok",
                ["security"] = auditRead ? "ok" : "hidden",
                ["recentActivity"] = auditRead ? "ok" : "hidden",
                ["needsAttention"] = "ok",
            };

            foreach (var (section, status) in expected)
            {
                body.GetProperty(section).GetProperty("status").GetString().Should().Be(status, section);
                body.GetProperty(section).GetProperty("data").ValueKind.Should().Be(
                    status == "ok" ? JsonValueKind.Object : JsonValueKind.Null,
                    $"only an ok section carries data, and {section} is {status}");
            }

            probe.Ran.Should().BeEquivalentTo(
                expected.Where(e => e.Value == "ok").Select(e => e.Key),
                "a hidden section is never started, so it cannot read a row the viewer may not see");

            var handed = probe.HandedToNeedsAttention;
            handed.Should().NotBeNull("needs attention runs last and is handed the other five results");
            StatusName(handed!.SystemHealth.Status).Should().Be(expected["systemHealth"]);
            StatusName(handed.Erp.Status).Should().Be(expected["erp"]);
            StatusName(handed.PeopleAndAccess.Status).Should().Be(expected["peopleAndAccess"]);
            StatusName(handed.Security.Status).Should().Be(expected["security"]);
            StatusName(handed.RecentActivity.Status).Should().Be(expected["recentActivity"]);

            probe.Requests.Should().HaveCount(probe.Ran.Count);
            probe.Requests.Should().OnlyContain(r => r.Viewer.UserId == viewerId,
                "every section is told who is asking");
            probe.Requests.Should().OnlyContain(r => r.Viewer.Permissions.SetEquals(granted),
                "the viewer's permissions are exactly the ones the token carries, read once for every section");
            probe.Requests.Select(r => r.AsOf).Distinct().Should().ContainSingle(
                "every section measures from the same instant");
            body.GetProperty("generatedAt").GetDateTimeOffset().Should().Be(probe.Requests.First().AsOf);
        }
        finally
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer, original);
        }
    }

    [Fact]
    public async Task A_section_that_throws_is_failed_and_the_others_are_still_ok()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = new Probe();
        await using var host = HostWith(probe, services =>
            services.AddScoped<IDashboardSectionHandler<DashboardPeopleAndAccessDto>,
                ThrowingSection<DashboardPeopleAndAccessDto>>());

        var response = await SignedInOn(host, admin).GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "one failing section must not fail the answer: " + text);

        text.Should().NotContain(probe.Secret, "a failed section carries none of the exception's text");

        var body = JsonDocument.Parse(text).RootElement;
        var failed = body.GetProperty("peopleAndAccess");
        failed.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status", "data"]);
        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);

        foreach (var section in Sections.Where(s => s != "peopleAndAccess"))
        {
            body.GetProperty(section).GetProperty("status").GetString().Should().Be("ok", section);
        }

        var handed = probe.HandedToNeedsAttention!;
        handed.PeopleAndAccess.Status.Should().Be(DashboardSectionStatus.Failed,
            "needs attention has to be able to say that a check could not run");
        new[] { handed.SystemHealth.Status, handed.Erp.Status, handed.Security.Status, handed.RecentActivity.Status }
            .Should().OnlyContain(s => s == DashboardSectionStatus.Ok);

        probe.Log.Entries.Should().Contain(e =>
                e.Level == LogLevel.Error
                && e.Message.Contains("peopleAndAccess")
                && e.Exception != null
                && e.Exception.Message == probe.Secret,
            "the failure is kept on the server, with the section's name, even though the answer says nothing about it");
    }

    [Fact]
    public async Task Needs_attention_that_throws_is_failed_on_its_own()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = new Probe();
        await using var host = HostWith(probe, services =>
            services.AddScoped<INeedsAttentionSectionHandler, ThrowingNeedsAttention>());

        var body = await DashboardAsync(host, admin);

        body.GetProperty("needsAttention").GetProperty("status").GetString().Should().Be("failed");
        body.GetProperty("needsAttention").GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        foreach (var section in Sections.Where(s => s != "needsAttention"))
        {
            body.GetProperty(section).GetProperty("status").GetString().Should().Be("ok", section);
        }

        probe.Log.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Message.Contains("needsAttention") && e.Exception != null);
    }

    [Fact]
    public async Task Sections_run_at_the_same_time_each_with_a_database_context_of_its_own()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = new Probe();
        await using var host = HostWith(probe, services =>
        {
            services.AddScoped<IDashboardSectionHandler<DashboardSystemHealthDto>, SimultaneousDatabaseSection<DashboardSystemHealthDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardErpDto>, SimultaneousDatabaseSection<DashboardErpDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardPeopleAndAccessDto>, SimultaneousDatabaseSection<DashboardPeopleAndAccessDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardSecurityDto>, SimultaneousDatabaseSection<DashboardSecurityDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardRecentActivityDto>, SimultaneousDatabaseSection<DashboardRecentActivityDto>>();
        });

        var body = await DashboardAsync(host, admin);

        var failures = probe.Log.Entries
            .Where(e => e.Level == LogLevel.Error && e.Exception != null)
            .Select(e => $"{e.Message} {e.Exception!.GetType().Name}: {e.Exception.Message}")
            .ToList();

        foreach (var section in Sections)
        {
            body.GetProperty(section).GetProperty("status").GetString().Should().Be("ok",
                $"{section} must neither wait for the others nor share their database context:\n"
                + string.Join("\n", failures));
        }

        probe.Contexts.Should().HaveCount(5);
        probe.Contexts.Values.Distinct().Should().HaveCount(5, "each section is resolved from a scope of its own");
    }

    private WebApplicationFactory<Program> HostWith(Probe probe, Action<IServiceCollection> overrides)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddSingleton<ILogger<GetAdminDashboardHandler>>(probe.Log);

            services.AddScoped<IDashboardSectionHandler<DashboardSystemHealthDto>, RecordingSection<DashboardSystemHealthDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardErpDto>, RecordingSection<DashboardErpDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardPeopleAndAccessDto>, RecordingSection<DashboardPeopleAndAccessDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardSecurityDto>, RecordingSection<DashboardSecurityDto>>();
            services.AddScoped<IDashboardSectionHandler<DashboardRecentActivityDto>, RecordingSection<DashboardRecentActivityDto>>();
            services.AddScoped<INeedsAttentionSectionHandler, RecordingNeedsAttention>();

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

    private static async Task<JsonElement> DashboardAsync(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var response = await SignedInOn(host, signedIn).GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string[]> PermissionsOfAsync(HttpClient admin, string role)
    {
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        return roles.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == role)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();
    }

    private static async Task SetPermissionsAsync(HttpClient admin, string role, string[] permissions)
    {
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{role}/permissions", new { permissions });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string StatusName(DashboardSectionStatus status) => status.ToString().ToLowerInvariant();

    private static TData Placeholder<TData>()
        where TData : class =>
        (TData)RuntimeHelpers.GetUninitializedObject(typeof(TData));

    private sealed class Probe
    {
        public string Secret { get; } = $"dashboard-internal-detail-{Guid.NewGuid():N}";

        public ConcurrentQueue<string> Ran { get; } = new();

        public ConcurrentQueue<DashboardRequest> Requests { get; } = new();

        public DashboardSectionResults? HandedToNeedsAttention { get; set; }

        public CountdownEvent Arrivals { get; } = new(5);

        public ConcurrentDictionary<string, Guid> Contexts { get; } = new();

        public LogRecorder Log { get; } = new();
    }

    private sealed class RecordingSection<TData>(Probe probe) : IDashboardSectionHandler<TData>
        where TData : class
    {
        public Task<TData> RunAsync(DashboardRequest request, CancellationToken ct)
        {
            probe.Ran.Enqueue(WireName[typeof(TData)]);
            probe.Requests.Enqueue(request);
            return Task.FromResult(Placeholder<TData>());
        }
    }

    private sealed class ThrowingSection<TData>(Probe probe) : IDashboardSectionHandler<TData>
        where TData : class
    {
        public Task<TData> RunAsync(DashboardRequest request, CancellationToken ct) =>
            throw new InvalidOperationException(probe.Secret);
    }

    private sealed class RecordingNeedsAttention(Probe probe) : INeedsAttentionSectionHandler
    {
        public Task<DashboardNeedsAttentionDto> RunAsync(
            DashboardRequest request, DashboardSectionResults others, CancellationToken ct)
        {
            probe.Ran.Enqueue("needsAttention");
            probe.Requests.Enqueue(request);
            probe.HandedToNeedsAttention = others;
            return Task.FromResult(Placeholder<DashboardNeedsAttentionDto>());
        }
    }

    private sealed class ThrowingNeedsAttention(Probe probe) : INeedsAttentionSectionHandler
    {
        public Task<DashboardNeedsAttentionDto> RunAsync(
            DashboardRequest request, DashboardSectionResults others, CancellationToken ct) =>
            throw new InvalidOperationException(probe.Secret);
    }

    // Blocks until all five sections have arrived, then holds its own database context busy for half a second.
    // The wait gives up after ten seconds rather than hanging the run, and gives up as a failed section, which is
    // what a frame that started the sections one after another is meant to produce here.
    private sealed class SimultaneousDatabaseSection<TData>(AppDbContext db, Probe probe) : IDashboardSectionHandler<TData>
        where TData : class
    {
        public async Task<TData> RunAsync(DashboardRequest request, CancellationToken ct)
        {
            probe.Arrivals.Signal();

            if (!probe.Arrivals.Wait(TimeSpan.FromSeconds(10), ct))
            {
                throw new TimeoutException($"{WireName[typeof(TData)]} ran while the other sections had not started");
            }

            probe.Contexts[WireName[typeof(TData)]] = db.ContextId.InstanceId;
            await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.5)", ct);

            return Placeholder<TData>();
        }
    }

    private sealed class LogRecorder : ILogger<GetAdminDashboardHandler>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}

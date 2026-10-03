// The ERP section of the administrator's dashboard, through the real route against a real database: the connection,
// the hourly sync and the supplier push, each figure seeded and read back over HTTP.
//
// Who sees the section at all is the frame's test, AdminDashboardFrameTests. This file is about what the section says
// to a viewer who may see it, a system administrator.
//
//
// EACH TEST RUNS ON A HOST OF ITS OWN WITH THE ERP SETTINGS IT NAMES
//
// The deployment's Erp settings, the fallback address and the WriteHosts list, are replaced for one host rather than
// written into the fixture, so each test says exactly what configuration it reads. The same host records every
// outbound request any HTTP client of the factory makes, so a test can say that none went to the ERP's host, and
// replaces the section's logger, so a failed part can be shown to have been logged by name.
//
//
// A MISSING MIGRATION IS SIMULATED IN THE DATABASE ITSELF
//
// A command interceptor renames, in the SQL sent to the database, the columns a migration added, so the database
// answers exactly as one without that migration would: an undefined column. The section is not told anything; it has
// to find out the way it would in an environment where #230 or #232 was never applied.
//
//
// THE SHARED DATABASE IS LEFT AS IT WAS FOUND
//
// The connection's row is shared by every class in the collection. Its columns are read before each test and written
// back after it. The suppliers seeded here are deleted afterwards. The audit trail is append-only, so an import this
// file leaves without an outcome is given a closing row of its own afterwards, dated just after its start, so that no
// later class finds an unfinished import that was only ever a test's. Counts are compared as differences from a reading
// taken before the seeding, and codes are seeded with request dates far older than any other class uses, so rows other
// classes leave do not move the assertions.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Admin.Dashboard;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpSectionTests(PostgresApiFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/v1/admin/dashboard";

    // The columns each migration added, as they are quoted in the SQL the section sends.
    private static readonly string[] NightlyErpSyncColumns =
        ["LastSyncAt", "LastSyncOutcome", "LastSyncSummary", "ErpDisabledState"];

    private static readonly string[] ErpSupplierPushColumns =
        ["ErpPushAttempts", "ErpPushLastError", "ErpPushNextAttemptAt", "ErpPushRequestedAt", "ErpPushStartedAt",
         "ErpPushStatus", "CreateSuppliersInErp", "DefaultSupplierGroup", "ExternalId"];

    private static readonly DateTimeOffset Parked = new(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<Guid> _suppliers = [];
    private readonly List<(Guid Correlation, DateTimeOffset StartedAt)> _openRuns = [];
    private ConnectionColumns? _original;

    private sealed record ConnectionColumns(
        string BaseUrl,
        bool IsEnabled,
        DateTimeOffset? LastTestedAt,
        bool? LastTestSucceeded,
        DateTimeOffset? LastSyncAt,
        IntegrationSyncOutcome? LastSyncOutcome,
        bool CreateSuppliersInErp,
        string? DefaultSupplierGroup);

    public async Task InitializeAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _original = await db.IntegrationConnections.AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new ConnectionColumns(
                c.BaseUrl, c.IsEnabled, c.LastTestedAt, c.LastTestSucceeded, c.LastSyncAt, c.LastSyncOutcome,
                c.CreateSuppliersInErp, c.DefaultSupplierGroup))
            .SingleAsync();
    }

    public async Task DisposeAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SetRowAsync(_original!);

        await db.Suppliers.Where(s => _suppliers.Contains(s.Id)).ExecuteDeleteAsync();

        foreach (var (correlation, startedAt) in _openRuns)
        {
            db.AuditLogs.Add(Row(SupplierAuditActions.ErpImportFailed, startedAt.AddMilliseconds(1), correlation,
                "Failed", """{"trigger":"Scheduled","failure":"Test","message":"closed by the test that opened it"}"""));
        }

        await db.SaveChangesAsync();
    }

    // ── the connection ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_connection_comes_from_the_settings_until_an_address_is_saved_on_the_row()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var options = Options(baseUrl: "https://erp-from-settings.test:8443/api?token=settings-secret-marker", enabled: true);

        await SetRowAsync(_original! with { BaseUrl = string.Empty, IsEnabled = false, LastTestedAt = null, LastTestSucceeded = null });
        await using (var host = Host(options))
        {
            var connection = Part(await DashboardAsync(host.Factory, admin), "connection");
            connection.GetProperty("source").GetString().Should().Be("Configuration");
            connection.GetProperty("enabled").GetBoolean().Should().BeTrue("the settings' switch goes with the settings' address");
            connection.GetProperty("host").GetString().Should().Be("erp-from-settings.test:8443");
            connection.GetProperty("https").GetBoolean().Should().BeTrue();
            connection.GetProperty("lastTestedAt").ValueKind.Should().Be(JsonValueKind.Null);
            connection.GetProperty("lastTestSucceeded").ValueKind.Should().Be(JsonValueKind.Null);

            await SameAsProviderAsync(host.Factory, connection);
        }

        var testedAt = Micro(DateTimeOffset.UtcNow.AddHours(-2));
        await SetRowAsync(_original! with
        {
            BaseUrl = "http://9.160.105.219:8001/erp/path-secret-marker",
            IsEnabled = false,
            LastTestedAt = testedAt,
            LastTestSucceeded = false,
        });
        await using (var host = Host(options))
        {
            var text = await DashboardTextAsync(host.Factory, admin);
            text.Should().NotContain("secret-marker", "only the host is shown, never the whole address");

            var connection = Part(JsonDocument.Parse(text).RootElement, "connection");
            connection.GetProperty("source").GetString().Should().Be("Database");
            connection.GetProperty("enabled").GetBoolean().Should().BeFalse("the row's switch goes with the row's address");
            connection.GetProperty("host").GetString().Should().Be("9.160.105.219:8001");
            connection.GetProperty("https").GetBoolean().Should().BeFalse("a plain-http address has to be visible as such");
            connection.GetProperty("lastTestedAt").GetDateTimeOffset().Should().BeCloseTo(testedAt, TimeSpan.FromMilliseconds(1));
            connection.GetProperty("lastTestSucceeded").GetBoolean().Should().BeFalse();

            await SameAsProviderAsync(host.Factory, connection);
        }

        await SetRowAsync(_original! with { BaseUrl = string.Empty, IsEnabled = true });
        await using (var host = Host(Options(baseUrl: string.Empty, enabled: true)))
        {
            var connection = Part(await DashboardAsync(host.Factory, admin), "connection");
            connection.GetProperty("source").GetString().Should().Be("None");
            connection.GetProperty("enabled").GetBoolean().Should().BeFalse("with no address there is nothing to be enabled");
            connection.GetProperty("host").ValueKind.Should().Be(JsonValueKind.Null);
            connection.GetProperty("https").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    // ── the hourly sync ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_last_run_is_the_one_the_connection_records_with_the_counts_of_its_own_closing_row()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var lastRun = Micro(DateTimeOffset.UtcNow.AddHours(-1));

        await SeedClosingRowsAsync(
            Row(SupplierAuditActions.ErpImportCompleted, lastRun.AddMilliseconds(1), Guid.NewGuid(), "NeedsAttention",
                """{"trigger":"Scheduled","erpSuppliers":41,"created":3,"updated":5,"suspended":2,"refused":1,"failed":4}"""),
            Row(SupplierAuditActions.ErpImportCompleted, lastRun.AddSeconds(30), Guid.NewGuid(), "Succeeded",
                """{"trigger":"Manual","erpSuppliers":99,"created":99,"updated":99,"suspended":99,"refused":99,"failed":99}"""));

        await SetRowAsync(_original! with
        {
            BaseUrl = "https://erp-sync.test", IsEnabled = true,
            LastSyncAt = lastRun, LastSyncOutcome = IntegrationSyncOutcome.NeedsAttention,
        });

        await using var host = Host(Options());
        var sync = Part(await DashboardAsync(host.Factory, admin), "sync");

        sync.GetProperty("lastRunAt").GetDateTimeOffset().Should().BeCloseTo(lastRun, TimeSpan.FromMilliseconds(1));
        sync.GetProperty("outcome").GetString().Should().Be("NeedsAttention");
        sync.GetProperty("trigger").GetString().Should().Be("Scheduled",
            "the counts belong to the run the connection records, its first closing row, not to a later one");
        var counts = sync.GetProperty("counts");
        counts.GetProperty("erpSuppliers").GetInt32().Should().Be(41);
        counts.GetProperty("created").GetInt32().Should().Be(3);
        counts.GetProperty("updated").GetInt32().Should().Be(5);
        counts.GetProperty("suspended").GetInt32().Should().Be(2);
        counts.GetProperty("refused").GetInt32().Should().Be(1);
        counts.GetProperty("failed").GetInt32().Should().Be(4);
        sync.GetProperty("stale").GetBoolean().Should().BeFalse("an hour is within the three hours an enabled sync may go");
    }

    [Fact]
    public async Task A_run_that_threw_has_its_outcome_and_trigger_and_no_counts()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var lastRun = Micro(DateTimeOffset.UtcNow.AddMinutes(-50));

        await SeedClosingRowsAsync(
            Row(SupplierAuditActions.ErpImportFailed, lastRun.AddMilliseconds(1), Guid.NewGuid(), "Failed",
                """{"trigger":"Manual","failure":"HttpRequestException","message":"refused"}"""));
        await SetRowAsync(_original! with
        {
            BaseUrl = "https://erp-sync.test", IsEnabled = true,
            LastSyncAt = lastRun, LastSyncOutcome = IntegrationSyncOutcome.Failed,
        });

        await using var host = Host(Options());
        var sync = Part(await DashboardAsync(host.Factory, admin), "sync");

        sync.GetProperty("outcome").GetString().Should().Be("Failed");
        sync.GetProperty("trigger").GetString().Should().Be("Manual");
        sync.GetProperty("counts").ValueKind.Should().Be(JsonValueKind.Null, "a run that threw returned no report");
    }

    [Fact]
    public async Task The_sync_is_stale_after_three_hours_only_while_the_connection_is_enabled()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await using var host = Host(Options());

        async Task<bool> StaleAsync(TimeSpan ago, bool enabled)
        {
            await SetRowAsync(_original! with
            {
                BaseUrl = "https://erp-sync.test", IsEnabled = enabled,
                LastSyncAt = Micro(DateTimeOffset.UtcNow - ago), LastSyncOutcome = IntegrationSyncOutcome.Succeeded,
            });
            return Part(await DashboardAsync(host.Factory, admin), "sync").GetProperty("stale").GetBoolean();
        }

        (await StaleAsync(TimeSpan.FromHours(3.1), enabled: true)).Should().BeTrue("no run has recorded an outcome in three hours");
        (await StaleAsync(TimeSpan.FromHours(2.9), enabled: true)).Should().BeFalse("a run recorded an outcome within three hours");
        (await StaleAsync(TimeSpan.FromHours(30), enabled: false)).Should().BeFalse("a disabled connection is not meant to sync");
    }

    [Fact]
    public async Task With_no_run_on_the_connection_the_latest_closing_row_on_the_trail_is_the_last_run()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var closedAt = Micro(DateTimeOffset.UtcNow.AddSeconds(-1));

        await SeedClosingRowsAsync(
            Row(SupplierAuditActions.ErpImportFailed, closedAt, Guid.NewGuid(), "Failed",
                """{"trigger":"Scheduled","failure":"Test","message":"no connection row"}"""));
        await SetRowAsync(_original! with
        {
            BaseUrl = "https://erp-sync.test", IsEnabled = true, LastSyncAt = null, LastSyncOutcome = null,
        });

        await using var host = Host(Options());
        var sync = Part(await DashboardAsync(host.Factory, admin), "sync");

        sync.GetProperty("lastRunAt").GetDateTimeOffset().Should().BeCloseTo(closedAt, TimeSpan.FromMilliseconds(1));
        sync.GetProperty("outcome").GetString().Should().Be("Failed");
        sync.GetProperty("trigger").GetString().Should().Be("Scheduled");
        sync.GetProperty("stale").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_import_that_started_over_thirty_minutes_ago_and_recorded_no_outcome_is_flagged()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var now = DateTimeOffset.UtcNow;
        await using var host = Host(Options());

        var closedRun = Micro(now.AddHours(-4.5));
        var openRun = Micro(now.AddHours(-4));
        var young = Micro(now.AddMinutes(-20));
        await SeedRunAsync(closedRun, closed: true);
        await SeedRunAsync(openRun, closed: false);
        await SeedRunAsync(young, closed: false);

        async Task<JsonElement> UnfinishedAsync(DateTimeOffset lastRunAt)
        {
            await SetRowAsync(_original! with
            {
                BaseUrl = "https://erp-sync.test", IsEnabled = true,
                LastSyncAt = lastRunAt, LastSyncOutcome = IntegrationSyncOutcome.Succeeded,
            });
            return Part(await DashboardAsync(host.Factory, admin), "sync").GetProperty("unfinishedRunStartedAt");
        }

        (await UnfinishedAsync(Micro(now.AddHours(-5)))).GetDateTimeOffset()
            .Should().BeCloseTo(openRun, TimeSpan.FromMilliseconds(1),
                "the earliest import after the last outcome with no closing row of its own; the earlier one closed");

        (await UnfinishedAsync(Micro(now.AddMinutes(-29)))).ValueKind.Should().Be(JsonValueKind.Null,
            "an outcome recorded after an import started settles it, and one that started twenty minutes ago is not late yet");
    }

    // ── the supplier push ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task While_the_switch_is_off_the_push_is_information_with_no_stalled_count()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await SetRowAsync(_original! with
        {
            BaseUrl = "http://9.160.105.219:8001", IsEnabled = false, CreateSuppliersInErp = false, DefaultSupplierGroup = null,
        });
        await using var host = Host(Options());
        var before = Part(await DashboardAsync(host.Factory, admin), "push");

        var seeded = await SeedPushSuppliersAsync();

        var push = Part(await DashboardAsync(host.Factory, admin), "push");
        push.GetProperty("switchOn").GetBoolean().Should().BeFalse();
        push.GetProperty("defaultGroup").ValueKind.Should().Be(JsonValueKind.Null);
        Delta(before, push, "waiting").Should().Be(4, "the four approved suppliers in service that are Requested or Linked");
        Delta(before, push, "failed").Should().Be(4);
        push.GetProperty("stalled").ValueKind.Should().Be(JsonValueKind.Null,
            "a push that is switched off is waiting, not late, so nothing is counted as stalled");
        Codes(push).Take(4).Should().Equal(
            [seeded.Failed[0], seeded.Failed[1], seeded.Failed[2], seeded.Failed[3]],
            "while the switch is off only the failed pushes are named, oldest request first");
    }

    [Fact]
    public async Task While_the_switch_is_on_overdue_and_abandoned_attempts_are_stalled()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await SetRowAsync(_original! with
        {
            BaseUrl = "http://9.160.105.219:8001", IsEnabled = false, CreateSuppliersInErp = true,
            DefaultSupplierGroup = "Local Suppliers - SYP",
        });
        await using var host = Host(Options());
        var before = Part(await DashboardAsync(host.Factory, admin), "push");

        var seeded = await SeedPushSuppliersAsync();

        var push = Part(await DashboardAsync(host.Factory, admin), "push");
        push.GetProperty("switchOn").GetBoolean().Should().BeTrue();
        push.GetProperty("defaultGroup").GetString().Should().Be("Local Suppliers - SYP");
        Delta(before, push, "waiting").Should().Be(4);
        Delta(before, push, "failed").Should().Be(4);
        Delta(before, push, "stalled").Should().Be(2,
            "one next attempt more than fifteen minutes overdue and one in-flight marker older than ten minutes; "
            + "the five-minute-late attempt, the young marker, the parked supplier and the suspended one are not stalled");
        Codes(push).Should().Equal(
            [seeded.Failed[0], seeded.Overdue, seeded.Failed[1], seeded.InFlight, seeded.Failed[2]],
            "failed and stalled together, oldest request first, five at most");
    }

    [Fact]
    public async Task Whether_the_host_is_on_WriteHosts_is_a_yes_or_no_matched_as_the_writer_matches_it()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        async Task<(bool OnList, string Text)> OnListAsync(string rowAddress, string[] writeHosts)
        {
            await SetRowAsync(_original! with { BaseUrl = rowAddress, IsEnabled = false });
            await using var host = Host(Options(writeHosts: writeHosts));
            var text = await DashboardTextAsync(host.Factory, admin);
            return (Part(JsonDocument.Parse(text).RootElement, "push").GetProperty("hostOnWriteHosts").GetBoolean(), text);
        }

        string[] list = ["erp-unrelated-write-host.example", "9.160.105.219:8001"];

        var (onList, text) = await OnListAsync("http://9.160.105.219:8001", list);
        onList.Should().BeTrue();
        text.Should().NotContain("erp-unrelated-write-host.example", "the list itself never leaves the configuration");

        (await OnListAsync("http://9.160.105.219:8002", list)).OnList.Should().BeFalse("the entry names one port");
        (await OnListAsync("http://9.160.105.219:8002", ["9.160.105.219"])).OnList.Should().BeTrue("an entry without a port allows any");
        (await OnListAsync("http://ERP.Example:8001", ["erp.example:8001"])).OnList.Should().BeTrue("hosts match in any letter case");
        (await OnListAsync(string.Empty, list)).OnList.Should().BeFalse("with no address there is no server to be listed");

        foreach (var (address, hosts) in new[]
                 {
                     ("http://9.160.105.219:8001", list), ("http://9.160.105.219:8002", list),
                     ("http://9.160.105.219:8002", new[] { "9.160.105.219" }),
                 })
        {
            (await OnListAsync(address, hosts)).OnList.Should().Be(ErpWriteHosts.Lists(hosts, address),
                "the dashboard and ErpSupplierRegistrar ask the same helper");
        }
    }

    // ── failing on its own, and never calling the ERP ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("connection")]
    [InlineData("NightlyErpSync")]
    [InlineData("ErpSupplierPush")]
    public async Task A_part_whose_columns_are_missing_fails_on_its_own(string missing)
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await SetRowAsync(_original! with { BaseUrl = "https://erp-sync.test", IsEnabled = true });

        var (columns, failed) = missing switch
        {
            "connection" => (new[] { "LastTestedAt" }, new[] { "connection" }),

            // The push's rule for an approved supplier in service reads ErpDisabledState, which #230 added.
            "NightlyErpSync" => (NightlyErpSyncColumns, new[] { "sync", "push" }),
            _ => (ErpSupplierPushColumns, new[] { "push" }),
        };

        await using var host = Host(Options(), missingColumns: columns);
        var text = await DashboardTextAsync(host.Factory, admin);
        var body = JsonDocument.Parse(text).RootElement;

        body.GetProperty("erp").GetProperty("status").GetString().Should().Be("ok",
            "a missing migration costs the part that needs it, not the section");
        foreach (var part in new[] { "connection", "sync", "push" })
        {
            var envelope = body.GetProperty("erp").GetProperty("data").GetProperty(part);
            var expected = failed.Contains(part) ? "failed" : "ok";
            envelope.GetProperty("status").GetString().Should().Be(expected, $"{part} with {missing} missing");
            envelope.GetProperty("data").ValueKind.Should().Be(
                expected == "ok" ? JsonValueKind.Object : JsonValueKind.Null, $"only an ok {part} carries data");
        }

        text.Should().NotContain("_not_migrated", "a failed part carries none of the database's error");
        foreach (var part in failed)
        {
            host.Log.Entries.Should().Contain(
                e => e.Level == LogLevel.Error && e.Message.Contains($"ERP {part}") && e.Exception != null,
                $"the failed {part} is logged on the server by name");
        }
    }

    [Fact]
    public async Task The_section_never_sends_a_request_to_the_ERP()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await SetRowAsync(_original! with
        {
            BaseUrl = "https://erp-never-called.test:8443", IsEnabled = true, CreateSuppliersInErp = true,
            DefaultSupplierGroup = "Local Suppliers - SYP",
        });
        await using var host = Host(Options(baseUrl: "https://erp-settings-never-called.test", enabled: true));

        var body = await DashboardAsync(host.Factory, admin);
        foreach (var part in new[] { "connection", "sync", "push" })
        {
            body.GetProperty("erp").GetProperty("data").GetProperty(part).GetProperty("status").GetString()
                .Should().Be("ok", part);
        }

        host.Outbound.Should().NotContain(
            uri => uri.Host.Contains("never-called"),
            "the dashboard reads rows and configuration only; asking the ERP every minute per tab is what it must not do");

        // The recorder sees what the ERP's own clients send, so the assertion above is not empty by construction.
        var probe = host.Factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("IErpSupplierSource");
        (await probe.GetAsync("https://erp-never-called.test:8443/api/resource/Supplier")).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Outbound.Should().Contain(uri => uri.Host == "erp-never-called.test");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record PushSeed(string[] Failed, string Overdue, string InFlight);

    // Four failed pushes and six suppliers the push works on, with request dates in the year 2000 so they come before
    // any supplier another class leaves: failed on days 1, 3, 5 and 6, an attempt twenty minutes overdue on day 2 and an
    // in-flight marker eleven minutes old on day 4. Not stalled: an attempt five minutes late with a marker five minutes
    // old, a parked one, and an overdue one that a person suspended, which is out of service and not waiting at all.
    private async Task<PushSeed> SeedPushSuppliersAsync()
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset Day(int day) => new(2000, 1, day, 0, 0, 0, TimeSpan.Zero);

        var failed = new[]
        {
            await SupplierAsync(SupplierErpPushStatus.Failed, Day(1), Parked, null),
            await SupplierAsync(SupplierErpPushStatus.Failed, Day(3), Parked, null),
            await SupplierAsync(SupplierErpPushStatus.Failed, Day(5), Parked, null),
            await SupplierAsync(SupplierErpPushStatus.Failed, Day(6), Parked, null),
        };
        var overdue = await SupplierAsync(SupplierErpPushStatus.Requested, Day(2), now.AddMinutes(-20), null);
        var inFlight = await SupplierAsync(SupplierErpPushStatus.Linked, Day(4), now.AddMinutes(-5), now.AddMinutes(-11));
        await SupplierAsync(SupplierErpPushStatus.Requested, Day(7), now.AddMinutes(-5), now.AddMinutes(-5));
        await SupplierAsync(SupplierErpPushStatus.Requested, Day(8), Parked, null);
        await SupplierAsync(
            SupplierErpPushStatus.Requested, Day(9), now.AddHours(-1), null, SupplierLifecycleState.Suspended);

        return new PushSeed(failed, overdue, inFlight);
    }

    private async Task<string> SupplierAsync(
        SupplierErpPushStatus push,
        DateTimeOffset requestedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? startedAt,
        SupplierLifecycleState lifecycle = SupplierLifecycleState.Active)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var code = $"SUP-DB-{Guid.NewGuid():N}"[..24];
        var supplier = Supplier.Register(
            code, "مورد لوحة", $"Dashboard {Guid.NewGuid():N}"[..20],
            $"RC-{Guid.NewGuid():N}"[..16], "Rana Haddad", $"dashboard-{Guid.NewGuid():N}@example.com");
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        _suppliers.Add(supplier.Id);

        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, lifecycle)
            .SetProperty(s => s.ErpDisabledState, SupplierErpDisabledState.NotDisabled)
            .SetProperty(s => s.ErpPushStatus, push)
            .SetProperty(s => s.ErpPushRequestedAt, requestedAt)
            .SetProperty(s => s.ErpPushNextAttemptAt, nextAttemptAt)
            .SetProperty(s => s.ErpPushStartedAt, startedAt));

        return code;
    }

    private async Task SeedRunAsync(DateTimeOffset startedAt, bool closed)
    {
        var correlation = Guid.NewGuid();
        var rows = new List<AuditLog> { Row(SupplierAuditActions.ErpImportRun, startedAt, correlation, null, null) };

        if (closed)
        {
            rows.Add(Row(SupplierAuditActions.ErpImportCompleted, startedAt.AddMinutes(2), correlation, "Succeeded",
                """{"trigger":"Scheduled","erpSuppliers":1,"created":0,"updated":0,"suspended":0,"refused":0,"failed":0}"""));
        }
        else
        {
            _openRuns.Add((correlation, startedAt));
        }

        await SeedClosingRowsAsync([.. rows]);
    }

    private async Task SeedClosingRowsAsync(params AuditLog[] rows)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogs.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static AuditLog Row(string action, DateTimeOffset at, Guid correlation, string? toState, string? changes) => new()
    {
        Id = Guid.CreateVersion7(),
        OccurredAt = at,
        ActorKind = AuditActorKind.System,
        ActorLabel = "system",
        AggregateType = "Supplier",
        AggregateId = Guid.Empty,
        Action = action,
        ToState = toState,
        Changes = changes,
        CorrelationId = correlation,
    };

    private async Task SetRowAsync(ConnectionColumns row)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.IntegrationConnections.Where(c => c.Key == IntegrationConnection.ErpKey).ExecuteUpdateAsync(set => set
            .SetProperty(c => c.BaseUrl, row.BaseUrl)
            .SetProperty(c => c.IsEnabled, row.IsEnabled)
            .SetProperty(c => c.LastTestedAt, row.LastTestedAt)
            .SetProperty(c => c.LastTestSucceeded, row.LastTestSucceeded)
            .SetProperty(c => c.LastSyncAt, row.LastSyncAt)
            .SetProperty(c => c.LastSyncOutcome, row.LastSyncOutcome)
            .SetProperty(c => c.CreateSuppliersInErp, row.CreateSuppliersInErp)
            .SetProperty(c => c.DefaultSupplierGroup, row.DefaultSupplierGroup));
    }

    // The precedence the section repeats must agree with ErpConnectionProvider's, the one every ERP client uses.
    private static async Task SameAsProviderAsync(WebApplicationFactory<Program> host, JsonElement connection)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IErpConnectionProvider>().CurrentAsync(default);

        current.Should().NotBeNull();
        connection.GetProperty("source").GetString().Should().Be(current!.Source.ToString());
        connection.GetProperty("enabled").GetBoolean().Should().Be(current.IsEnabled);
        connection.GetProperty("host").GetString().Should().Be(new Uri(current.BaseUrl).Authority);
    }

    private static ErpOptions Options(string baseUrl = "", bool enabled = false, string[]? writeHosts = null) => new()
    {
        Enabled = enabled,
        BaseUrl = baseUrl,
        ApiKey = "dashboard-test-key",
        ApiSecret = "dashboard-test-secret",
        Company = "Seven Gates",
        WriteHosts = writeHosts ?? [],
    };

    private static DateTimeOffset Micro(DateTimeOffset at) => new(at.Ticks - (at.Ticks % 10), at.Offset);

    private static JsonElement Part(JsonElement body, string part)
    {
        var erp = body.GetProperty("erp");
        erp.GetProperty("status").GetString().Should().Be("ok");
        var envelope = erp.GetProperty("data").GetProperty(part);
        envelope.GetProperty("status").GetString().Should().Be("ok", $"the ERP {part} should have been read");
        return envelope.GetProperty("data");
    }

    private static int Delta(JsonElement before, JsonElement after, string figure) =>
        after.GetProperty(figure).GetInt32()
        - (before.GetProperty(figure).ValueKind == JsonValueKind.Null ? 0 : before.GetProperty(figure).GetInt32());

    private static string[] Codes(JsonElement push) =>
        push.GetProperty("referenceCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();

    private async Task<JsonElement> DashboardAsync(WebApplicationFactory<Program> host, HttpClient signedIn) =>
        JsonDocument.Parse(await DashboardTextAsync(host, signedIn)).RootElement;

    private static async Task<string> DashboardTextAsync(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        var response = await client.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return text;
    }

    private sealed class TestHost(WebApplicationFactory<Program> factory, LogRecorder log, ConcurrentQueue<Uri> outbound)
        : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public LogRecorder Log { get; } = log;

        public ConcurrentQueue<Uri> Outbound { get; } = outbound;

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private TestHost Host(ErpOptions erp, string[]? missingColumns = null)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();
        var log = new LogRecorder();
        var outbound = new ConcurrentQueue<Uri>();

        var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IOptions<ErpOptions>>(Microsoft.Extensions.Options.Options.Create(erp));
            services.AddSingleton<ILogger<ErpSectionHandler>>(log);
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(outbound)));

            if (missingColumns is not null)
            {
                services.ConfigureDbContext<AppDbContext>(options =>
                    options.AddInterceptors(new MissingColumns(missingColumns)));
            }

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
        }));

        return new TestHost(factory, log, outbound);
    }

    // Answers every outbound request with 503 after noting where it was going, so nothing leaves the test.
    private sealed class RecordingHandler(ConcurrentQueue<Uri> outbound) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            outbound.Enqueue(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    // Renames the given columns in every command, so the database answers as one that never had them.
    private sealed class MissingColumns(string[] columns) : DbCommandInterceptor
    {
        private void Rename(DbCommand command)
        {
            foreach (var column in columns)
            {
                command.CommandText = command.CommandText.Replace($"\"{column}\"", $"\"{column}_not_migrated\"");
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Rename(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Rename(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
        {
            Rename(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LogRecorder : ILogger<ErpSectionHandler>
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

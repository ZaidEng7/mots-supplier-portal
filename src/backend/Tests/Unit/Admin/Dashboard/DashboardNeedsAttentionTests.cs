// The rules that turn the dashboard's other sections into needs attention items, tested on results built by hand.
//
// Every test starts from a quiet dashboard, one in which every section is ok and nothing should fire, and changes
// one thing. The quiet dashboard is not made of zeros: the figures that must never raise an item, such as emails
// waiting to be retried, the outbox's pending count or suppliers waiting for the push, are set to something other
// than zero, so a rule that read the wrong figure fires here rather than passing on a lucky zero.
//
// The viewer holds every permission unless a test says otherwise, so a link that is missing is missing because
// the rule left it out, not because the viewer could not follow it.

namespace MotsSupplierPortal.Tests.Unit.Admin.Dashboard;

using FluentAssertions;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;
using Xunit;
using Checks = MotsSupplierPortal.Application.Admin.Dashboard.DashboardNeedsAttentionChecks;

public sealed class DashboardNeedsAttentionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DashboardViewer Everything = Viewer([.. Permissions.All]);

    private const string Operations = DashboardNeedsAttention.OperationsPage;
    private const string Integrations = DashboardNeedsAttention.IntegrationsPage;
    private const string ErpImport = DashboardNeedsAttention.ErpImportPage;

    private static DashboardViewer Viewer(params string[] permissions) =>
        new(Guid.NewGuid(), permissions.ToHashSet(StringComparer.Ordinal));

    private static DashboardJobDto Job(string id, DashboardJobVerdict verdict) =>
        new(id, verdict, 15, "Succeeded", AsOf.AddMinutes(-3), AsOf.AddMinutes(2), Operations);

    private static readonly DashboardSystemHealthDto QuietHealth = new(
        Jobs: new DashboardJobsDto(true, [Job("a", DashboardJobVerdict.Ok), Job("b", DashboardJobVerdict.Ok)]),
        Queue: new DashboardJobQueueDto(Enqueued: 4, Processing: 1, Retrying: 2, Failed: 3, LiveServers: 1, HeartbeatWithinMinutes: 5),
        Email: new DashboardEmailDto(FailedInWindow: 0, Retrying: 6, WindowDays: 7),
        Outbox: new DashboardOutboxDto(Pending: 8, Stuck: 0, StuckAfterMinutes: 15, Failed: 0, OldestPendingAt: AsOf.AddMinutes(-1)),
        Scans: new DashboardStuckScansDto(Stuck: 0, StuckAfterMinutes: 15),
        PendingMigrations: [],
        ReferenceLists: [new ReferenceTableHealthDto("governorates", 14, 2), new ReferenceTableHealthDto("currencies", 3, 0)],
        PurchaseOrderTransport: new DashboardPurchaseOrderTransportDto(false, 0),
        ObjectStorage: new DashboardObjectStorageDto(true));

    private static readonly DashboardErpConnectionDto QuietConnection =
        new(DashboardErpSource.Database, true, "erp.example", true, AsOf.AddDays(-1), true);

    private static readonly DashboardErpSyncDto QuietSync =
        new(AsOf.AddMinutes(-20), IntegrationSyncOutcome.Succeeded, null, null, false, null);

    private static readonly DashboardErpPushDto QuietPush =
        new(SwitchOn: true, DefaultGroup: "LOCAL", HostOnWriteHosts: true, Waiting: 9, Failed: 0, Stalled: 0, ReferenceCodes: []);

    private static readonly DashboardPeopleAndAccessDto QuietPeople = new(
        Staff: new DashboardAccountsDto(20, 3, 19, 6, 5, 4, LockedOut: 0, CannotSignIn: 0),
        Suppliers: new DashboardAccountsDto(200, 30, 190, 60, 50, 40, LockedOut: 0, CannotSignIn: 0),
        ActiveUsersByRole: [],
        StaffInvitedNeverSignedIn: new DashboardStaffInvitedNeverSignedInDto(LinkStillValid: 2, LinkExpired: 0, NoLinkYet: 1, LinkUsed: 3),
        TwoFactorRequiredRoles: [Roles.SystemAdmin],
        SupplierLoginsOnPlaceholderAddresses: 11,
        OrganisationsByType: []);

    private static readonly DashboardSecurityDto QuietSecurity = new(
        AsOf.AddDays(-30),
        [new DashboardSecurityEventCountDto("login_failed", 4, 30, false), new DashboardSecurityEventCountDto("password_reset", 1, 2, false)],
        []);

    private static DashboardErpDto Erp(
        DashboardErpConnectionDto? connection = null, DashboardErpSyncDto? sync = null, DashboardErpPushDto? push = null) =>
        new(
            DashboardSection<DashboardErpConnectionDto>.Ok(connection ?? QuietConnection),
            DashboardSection<DashboardErpSyncDto>.Ok(sync ?? QuietSync),
            DashboardSection<DashboardErpPushDto>.Ok(push ?? QuietPush));

    private static DashboardSectionResults Quiet(
        DashboardSystemHealthDto? health = null,
        DashboardErpDto? erp = null,
        DashboardPeopleAndAccessDto? people = null,
        DashboardSecurityDto? security = null) =>
        new(
            DashboardSection<DashboardSystemHealthDto>.Ok(health ?? QuietHealth),
            DashboardSection<DashboardErpDto>.Ok(erp ?? Erp()),
            DashboardSection<DashboardPeopleAndAccessDto>.Ok(people ?? QuietPeople),
            DashboardSection<DashboardSecurityDto>.Ok(security ?? QuietSecurity),
            DashboardSection<DashboardRecentActivityDto>.Ok(new DashboardRecentActivityDto(0, 0, [])));

    private static DashboardNeedsAttentionDto Compute(
        DashboardSectionResults results, DashboardViewer? viewer = null, int? overdueReviews = 0) =>
        DashboardNeedsAttention.Compute(viewer ?? Everything, results, overdueReviews);

    private static DashboardPeopleAndAccessDto People(
        int lockedStaff = 0, int lockedSuppliers = 0, int lapsed = 0, int staffCannot = 0, int suppliersCannot = 0) =>
        QuietPeople with
        {
            Staff = QuietPeople.Staff with { LockedOut = lockedStaff, CannotSignIn = staffCannot },
            Suppliers = QuietPeople.Suppliers with { LockedOut = lockedSuppliers, CannotSignIn = suppliersCannot },
            StaffInvitedNeverSignedIn = QuietPeople.StaffInvitedNeverSignedIn with { LinkExpired = lapsed },
        };

    [Fact]
    public void A_quiet_dashboard_is_all_clear()
    {
        var result = Compute(Quiet());

        result.Items.Should().BeEmpty();
        result.ChecksNotRun.Should().BeEmpty();
        result.AllClear.Should().BeTrue();
    }

    // Each case changes one thing on the quiet dashboard and names the one item it must raise, with its count and
    // link for a viewer holding everything. ERP items link to the import page because that viewer holds
    // supplier.import.run.
    public static TheoryData<string, DashboardSectionResults, string, int?, string?> EachCheck() => new()
    {
        { "a late job", Quiet(health: QuietHealth with { Jobs = new DashboardJobsDto(true, [Job("a", DashboardJobVerdict.Late), Job("b", DashboardJobVerdict.Ok)]) }), Checks.JobsNeedAttention, 1, Operations },
        {
            "late, failed, retrying and missing jobs, beside a disabled one",
            Quiet(health: QuietHealth with
            {
                Jobs = new DashboardJobsDto(true,
                [
                    Job("a", DashboardJobVerdict.Late), Job("b", DashboardJobVerdict.Failed), Job("c", DashboardJobVerdict.Retrying),
                    Job("d", DashboardJobVerdict.Missing), Job("e", DashboardJobVerdict.Disabled), Job("f", DashboardJobVerdict.Ok),
                ]),
            }),
            Checks.JobsNeedAttention, 4, Operations
        },
        { "no live job server", Quiet(health: QuietHealth with { Queue = QuietHealth.Queue with { LiveServers = 0 } }), Checks.NoLiveJobServers, null, Operations },
        { "failed emails", Quiet(health: QuietHealth with { Email = QuietHealth.Email with { FailedInWindow = 3 } }), Checks.EmailsFailed, 3, Operations },
        { "a stuck outbox", Quiet(health: QuietHealth with { Outbox = QuietHealth.Outbox with { Stuck = 2 } }), Checks.OutboxStuck, 2, Operations },
        { "a failed outbox", Quiet(health: QuietHealth with { Outbox = QuietHealth.Outbox with { Failed = 5 } }), Checks.OutboxFailed, 5, Operations },
        { "failed purchase-order sends", Quiet(health: QuietHealth with { PurchaseOrderTransport = new DashboardPurchaseOrderTransportDto(true, 6) }), Checks.PurchaseOrderSendsFailed, 6, Operations },
        { "stuck scans", Quiet(health: QuietHealth with { Scans = QuietHealth.Scans with { Stuck = 7 } }), Checks.ScansStuck, 7, Operations },
        { "pending migrations", Quiet(health: QuietHealth with { PendingMigrations = ["20260101_One", "20260102_Two"] }), Checks.MigrationsPending, 2, null },
        { "an unreachable object store", Quiet(health: QuietHealth with { ObjectStorage = new DashboardObjectStorageDto(false) }), Checks.ObjectStorageUnreachable, null, Operations },
        {
            "empty reference lists",
            Quiet(health: QuietHealth with
            {
                ReferenceLists = [new ReferenceTableHealthDto("governorates", 0, 14), new ReferenceTableHealthDto("currencies", 3, 0), new ReferenceTableHealthDto("banks", 0, 0)],
            }),
            Checks.ReferenceListsEmpty, 2, DashboardNeedsAttention.ReferencePage
        },
        { "a failed connection test", Quiet(erp: Erp(connection: QuietConnection with { LastTestSucceeded = false })), Checks.ErpConnectionTestFailed, null, ErpImport },
        { "a plain-http address", Quiet(erp: Erp(connection: QuietConnection with { Https = false })), Checks.ErpAddressNotHttps, null, ErpImport },
        { "a failed sync", Quiet(erp: Erp(sync: QuietSync with { Outcome = IntegrationSyncOutcome.Failed })), Checks.ErpSyncFailed, null, ErpImport },
        { "a stale sync", Quiet(erp: Erp(sync: QuietSync with { Stale = true })), Checks.ErpSyncStale, null, ErpImport },
        { "an unfinished import", Quiet(erp: Erp(sync: QuietSync with { UnfinishedRunStartedAt = AsOf.AddHours(-1) })), Checks.ErpImportUnfinished, null, ErpImport },
        { "a push host not on WriteHosts", Quiet(erp: Erp(push: QuietPush with { HostOnWriteHosts = false })), Checks.ErpPushHostNotOnWriteHosts, null, ErpImport },
        { "a push with no group", Quiet(erp: Erp(push: QuietPush with { DefaultGroup = null })), Checks.ErpPushNoDefaultGroup, null, ErpImport },
        { "a push with a blank group", Quiet(erp: Erp(push: QuietPush with { DefaultGroup = "  " })), Checks.ErpPushNoDefaultGroup, null, ErpImport },
        { "failed pushes", Quiet(erp: Erp(push: QuietPush with { Failed = 2, ReferenceCodes = ["SUP-1", "SUP-2"] })), Checks.ErpPushFailedOrStalled, 2, ErpImport },
        { "stalled pushes", Quiet(erp: Erp(push: QuietPush with { Stalled = 3, ReferenceCodes = ["SUP-1"] })), Checks.ErpPushFailedOrStalled, 3, ErpImport },
        { "failed and stalled pushes", Quiet(erp: Erp(push: QuietPush with { Failed = 2, Stalled = 3, ReferenceCodes = ["SUP-1"] })), Checks.ErpPushFailedOrStalled, 5, ErpImport },
        { "locked-out staff", Quiet(people: People(lockedStaff: 2)), Checks.StaffLockedOut, 2, DashboardNeedsAttention.StaffPage },
        { "lapsed staff invitations", Quiet(people: People(lapsed: 3)), Checks.StaffInvitationsLapsed, 3, DashboardNeedsAttention.StaffPage },
        { "staff who cannot sign in", Quiet(people: People(staffCannot: 4)), Checks.StaffCannotSignIn, 4, DashboardNeedsAttention.StaffPage },
        { "locked-out supplier logins", Quiet(people: People(lockedSuppliers: 5)), Checks.SupplierLoginsLockedOut, 5, null },
        { "supplier logins who cannot sign in", Quiet(people: People(suppliersCannot: 6)), Checks.SupplierLoginsCannotSignIn, 6, null },
        {
            "a spiking security event",
            Quiet(security: QuietSecurity with
            {
                Events = [new DashboardSecurityEventCountDto("login_failed", 40, 45, true), new DashboardSecurityEventCountDto("password_reset", 1, 2, false)],
            }),
            Checks.SecuritySpike, 1, DashboardNeedsAttention.AuditPage
        },
    };

    [Theory]
    [MemberData(nameof(EachCheck))]
    public void Each_check_fires_on_its_own_figure(
        string because, DashboardSectionResults results, string key, int? count, string? link)
    {
        var result = Compute(results);

        var item = result.Items.Should().ContainSingle(because).Subject;
        item.Key.Should().Be(key, because);
        item.Count.Should().Be(count, because);
        item.Link.Should().Be(link, because);
        result.ChecksNotRun.Should().BeEmpty(because);
        result.AllClear.Should().BeFalse(because);
    }

    [Fact]
    public void Disabled_and_ok_jobs_raise_nothing()
    {
        var health = QuietHealth with
        {
            Jobs = new DashboardJobsDto(false, [Job("a", DashboardJobVerdict.Disabled), Job("b", DashboardJobVerdict.Disabled)]),
        };

        Compute(Quiet(health: health)).AllClear.Should().BeTrue("switching recurring jobs off is a choice, not a fault");
    }

    [Fact]
    public void An_address_that_is_not_usable_and_a_connection_never_tested_raise_nothing()
    {
        var connection = QuietConnection with { Host = null, Https = null, LastTestedAt = null, LastTestSucceeded = null };

        Compute(Quiet(erp: Erp(connection: connection))).AllClear.Should().BeTrue(
            "no usable address is not plain http, and a test nobody ran did not fail");
    }

    [Fact]
    public void Nothing_about_the_push_is_an_alert_while_its_switch_is_off()
    {
        var off = new DashboardErpPushDto(
            SwitchOn: false, DefaultGroup: null, HostOnWriteHosts: false, Waiting: 40, Failed: 3, Stalled: null,
            ReferenceCodes: ["SUP-1", "SUP-2", "SUP-3"]);

        var result = Compute(Quiet(erp: Erp(push: off)));

        result.Items.Should().BeEmpty("Q10: with the switch off nothing about the push is ever an alert");
        result.ChecksNotRun.Should().BeEmpty("the push part ran; its checks simply cannot fire while it is off");
        result.AllClear.Should().BeTrue();
    }

    [Fact]
    public void Failed_and_stalled_pushes_carry_at_most_five_codes()
    {
        var push = QuietPush with { Failed = 4, Stalled = 3, ReferenceCodes = ["S-1", "S-2", "S-3", "S-4", "S-5", "S-6", "S-7"] };

        var item = Compute(Quiet(erp: Erp(push: push))).Items.Single();

        item.References.Select(r => r.Code).Should().Equal("S-1", "S-2", "S-3", "S-4", "S-5");
        item.References.Select(r => r.Link).Should().Equal(
            "/back-office/review/S-1", "/back-office/review/S-2", "/back-office/review/S-3",
            "/back-office/review/S-4", "/back-office/review/S-5");
    }

    [Fact]
    public void A_code_is_linked_only_for_a_viewer_who_holds_admin_integrations_manage_and_the_review_page()
    {
        var push = QuietPush with { Failed = 1, ReferenceCodes = ["SUP 1/A"] };
        var results = Quiet(erp: Erp(push: push));

        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.AdminIntegrationsManage, Permissions.SupplierReview))
            .Items.Single().References.Single().Link.Should().Be("/back-office/review/SUP%201%2FA",
                "the code is one path segment, escaped");

        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.SupplierReview))
            .Items.Single().References.Single().Link.Should().BeNull("without admin.integrations.manage no code is linked");

        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.AdminIntegrationsManage))
            .Items.Single().References.Single().Link.Should().BeNull("the review page refuses a viewer without supplier.review");

        Compute(results, Viewer(Permissions.AdminUsersManage))
            .Items.Single().References.Single().Code.Should().Be("SUP 1/A", "the code is shown even when it cannot be linked");
    }

    [Fact]
    public void Items_other_than_the_push_carry_no_codes()
    {
        var push = QuietPush with { HostOnWriteHosts = false, ReferenceCodes = ["SUP-1"] };

        Compute(Quiet(erp: Erp(push: push))).Items.Single().References.Should().BeEmpty();
    }

    [Fact]
    public void Erp_items_link_to_the_import_page_for_import_holders_and_to_integrations_otherwise()
    {
        var results = Quiet(erp: Erp(sync: QuietSync with { Stale = true }));

        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.AdminIntegrationsManage, Permissions.SupplierImportRun))
            .Items.Single().Link.Should().Be(ErpImport);
        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.AdminIntegrationsManage))
            .Items.Single().Link.Should().Be(Integrations);
        Compute(results, Viewer(Permissions.AdminUsersManage, Permissions.SupplierImportRun))
            .Items.Single().Link.Should().Be(ErpImport);
        Compute(results, Viewer(Permissions.AdminUsersManage))
            .Items.Single().Link.Should().BeNull();
    }

    [Fact]
    public void Every_link_is_withheld_from_a_viewer_without_its_page_permission()
    {
        var results = Quiet(
            health: QuietHealth with
            {
                Scans = QuietHealth.Scans with { Stuck = 1 },
                ReferenceLists = [new ReferenceTableHealthDto("banks", 0, 0)],
            },
            erp: Erp(sync: QuietSync with { Stale = true }),
            people: People(lockedStaff: 1),
            security: QuietSecurity with { Events = [new DashboardSecurityEventCountDto("login_failed", 40, 45, true)] });

        var withEverything = Compute(results, overdueReviews: 2);
        withEverything.Items.Select(i => (i.Key, i.Link)).Should().Equal(
            (Checks.ScansStuck, Operations),
            (Checks.ReferenceListsEmpty, DashboardNeedsAttention.ReferencePage),
            (Checks.ErpSyncStale, ErpImport),
            (Checks.StaffLockedOut, DashboardNeedsAttention.StaffPage),
            (Checks.SecuritySpike, DashboardNeedsAttention.AuditPage),
            (Checks.ReviewDeadlinePassed, DashboardNeedsAttention.ReviewQueuePage));

        var withNothing = Compute(results, Viewer(), overdueReviews: 2);
        withNothing.Items.Select(i => i.Key).Should().Equal(withEverything.Items.Select(i => i.Key),
            "a missing permission takes away the link, never the item");
        withNothing.Items.Should().OnlyContain(i => i.Link == null);

        foreach (var (permission, keys) in new (string, string[])[]
                 {
                     (Permissions.AdminUsersManage, [Checks.ScansStuck, Checks.StaffLockedOut]),
                     (Permissions.ReferenceDataManage, [Checks.ReferenceListsEmpty]),
                     (Permissions.AdminIntegrationsManage, [Checks.ErpSyncStale]),
                     (Permissions.AuditRead, [Checks.SecuritySpike]),
                     (Permissions.SupplierReview, [Checks.ReviewDeadlinePassed]),
                 })
        {
            var all = Permissions.All.Where(p => p != permission && p != Permissions.SupplierImportRun).ToArray();
            var withoutIt = Compute(results, Viewer(all), overdueReviews: 2);

            withoutIt.Items.Where(i => i.Link == null).Select(i => i.Key).Should().BeEquivalentTo(keys,
                $"taking {permission} away takes away exactly the links that need it");
        }
    }

    [Fact]
    public void A_hidden_section_neither_fires_nor_counts_as_not_run()
    {
        var results = Quiet() with
        {
            Erp = DashboardSection<DashboardErpDto>.Hidden(),
            Security = DashboardSection<DashboardSecurityDto>.Hidden(),
            RecentActivity = DashboardSection<DashboardRecentActivityDto>.Hidden(),
        };

        var result = Compute(results);

        result.Items.Should().BeEmpty();
        result.ChecksNotRun.Should().BeEmpty("a hidden section's checks are not part of this viewer's dashboard");
        result.AllClear.Should().BeTrue("otherwise a viewer without audit.read could never be told all clear");
    }

    [Fact]
    public void Every_section_hidden_is_still_all_clear_when_the_review_deadline_is_met()
    {
        var results = new DashboardSectionResults(
            DashboardSection<DashboardSystemHealthDto>.Hidden(),
            DashboardSection<DashboardErpDto>.Hidden(),
            DashboardSection<DashboardPeopleAndAccessDto>.Hidden(),
            DashboardSection<DashboardSecurityDto>.Hidden(),
            DashboardSection<DashboardRecentActivityDto>.Hidden());

        Compute(results).AllClear.Should().BeTrue();
        Compute(results, overdueReviews: 1).AllClear.Should().BeFalse();
    }

    public static TheoryData<string, Func<DashboardSectionResults, DashboardSectionResults>, string[]> EachFailure() => new()
    {
        { "systemHealth", r => r with { SystemHealth = DashboardSection<DashboardSystemHealthDto>.Failed() }, [.. Checks.SystemHealth] },
        { "erp", r => r with { Erp = DashboardSection<DashboardErpDto>.Failed() }, [.. Checks.ErpConnection, .. Checks.ErpSync, .. Checks.ErpPush] },
        { "erp connection", r => r with { Erp = DashboardSection<DashboardErpDto>.Ok(Erp() with { Connection = DashboardSection<DashboardErpConnectionDto>.Failed() }) }, [.. Checks.ErpConnection] },
        { "erp sync", r => r with { Erp = DashboardSection<DashboardErpDto>.Ok(Erp() with { Sync = DashboardSection<DashboardErpSyncDto>.Failed() }) }, [.. Checks.ErpSync] },
        { "erp push", r => r with { Erp = DashboardSection<DashboardErpDto>.Ok(Erp() with { Push = DashboardSection<DashboardErpPushDto>.Failed() }) }, [.. Checks.ErpPush] },
        { "peopleAndAccess", r => r with { PeopleAndAccess = DashboardSection<DashboardPeopleAndAccessDto>.Failed() }, [.. Checks.PeopleAndAccess] },
        { "security", r => r with { Security = DashboardSection<DashboardSecurityDto>.Failed() }, [.. Checks.Security] },
        { "recentActivity", r => r with { RecentActivity = DashboardSection<DashboardRecentActivityDto>.Failed() }, [] },
    };

    [Theory]
    [MemberData(nameof(EachFailure))]
    public void A_failed_section_names_exactly_its_own_checks_as_not_run(
        string section, Func<DashboardSectionResults, DashboardSectionResults> fail, string[] expected)
    {
        var result = Compute(fail(Quiet()));

        result.ChecksNotRun.Should().BeEquivalentTo(expected, section);
        result.Items.Should().BeEmpty(section);
        result.AllClear.Should().Be(expected.Length == 0,
            $"{section} failing leaves {expected.Length} checks unrun, and all clear needs every visible check to have run");
    }

    [Fact]
    public void A_failed_section_does_not_silence_the_others()
    {
        var results = Quiet(people: People(staffCannot: 2)) with
        {
            SystemHealth = DashboardSection<DashboardSystemHealthDto>.Failed(),
            Erp = DashboardSection<DashboardErpDto>.Ok(Erp(sync: QuietSync with { Stale = true }) with
            {
                Push = DashboardSection<DashboardErpPushDto>.Failed(),
            }),
        };

        var result = Compute(results);

        result.Items.Select(i => i.Key).Should().Equal(Checks.ErpSyncStale, Checks.StaffCannotSignIn);
        result.ChecksNotRun.Should().Equal([.. Checks.SystemHealth, .. Checks.ErpPush]);
        result.AllClear.Should().BeFalse();
    }

    [Fact]
    public void The_review_deadline_fires_on_a_count_and_is_not_run_when_its_query_failed()
    {
        var overdue = Compute(Quiet(), overdueReviews: 3);
        var item = overdue.Items.Should().ContainSingle().Subject;
        item.Should().Be(new DashboardAttentionItemDto(
            Checks.ReviewDeadlinePassed, 3, DashboardNeedsAttention.ReviewQueuePage, []));

        var failed = Compute(Quiet(), overdueReviews: null);
        failed.Items.Should().BeEmpty();
        failed.ChecksNotRun.Should().Equal(Checks.ReviewDeadlinePassed);
        failed.AllClear.Should().BeFalse("a check that could not run is not a check that passed");

        Compute(Quiet(), overdueReviews: 0).AllClear.Should().BeTrue();
    }

    [Fact]
    public void Every_key_is_lower_snake_case_and_listed_once()
    {
        string[] all =
        [
            .. Checks.SystemHealth, .. Checks.ErpConnection, .. Checks.ErpSync, .. Checks.ErpPush,
            .. Checks.PeopleAndAccess, .. Checks.Security, .. Checks.ReviewDeadline,
        ];

        all.Should().OnlyHaveUniqueItems();
        all.Should().OnlyContain(k => System.Text.RegularExpressions.Regex.IsMatch(k, "^[a-z]+(_[a-z]+)*$"));
    }
}

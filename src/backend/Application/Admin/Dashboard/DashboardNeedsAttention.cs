// The rules that turn the other sections' results into the needs attention section: which checks fired, where each
// one sends the viewer, and which could not run. The record they build, and what "all clear" means, is described in
// DashboardNeedsAttentionDto.cs.
//
// Everything here is a pure function of the viewer, the five results and the one figure the section counts for
// itself, the overdue reviews. It reads nothing and calls nothing, so every rule can be tested by building the
// results by hand.
//
//
// A SECTION THAT IS HIDDEN, FAILED OR OK
//
// Hidden: its checks are left out entirely, neither fired nor not run, as the record's header explains.
// Failed: every check it carries is named in ChecksNotRun. Ok: each check is judged on the section's data.
//
// The ERP section is judged part by part. Its connection, sync and push fail on their own, so a failed part puts
// only its own checks in ChecksNotRun, and the whole ERP section failing puts all three parts' checks there.
//
// The review deadline has no section. It is passed in as a number, or as null when its own query failed, and then
// its check is not run like any other.
//
//
// THE CHECKS, AND WHERE EACH ONE LINKS
//
// A link is the root of the page somebody acts on, given only when the viewer holds the permission that page's
// endpoints are gated on, the same one the navigation shows it under:
//
//   /back-office/operations     admin.users.manage
//   /back-office/reference      reference.manage
//   /back-office/integrations   admin.integrations.manage
//   /back-office/erp-import     supplier.import.run
//   /back-office/staff          admin.users.manage
//   /back-office/audit          audit.read
//   /back-office/review         supplier.review, the queue's root and each supplier's page beneath it
//
// From system health, to the operations page:
//
//   jobs_need_attention         jobs whose verdict is late, failed, retrying or missing; disabled is not an alert,
//                               because switching recurring jobs off is a deployment's choice
//   no_live_job_servers         no job server has reported in, so no background job of any kind is running
//   emails_failed               emails that failed within the section's window
//   outbox_stuck                outbox messages pending for longer than the section's threshold
//   outbox_failed               outbox messages that failed
//   purchase_order_sends_failed awards whose purchase order could not be sent
//   scans_stuck                 supplier documents still waiting for the virus scanner
//   object_storage_unreachable  the object store did not answer its ping
//
//   migrations_pending          migrations not yet applied, counted, with NO link: the operations page does not
//                               show them, and the dashboard's own system health block names each one, which is
//                               what a person applying them by hand needs
//
// Also from system health, to the reference data page:
//
//   reference_lists_empty       reference lists with no active code, each of which blocks registration
//
// From the ERP section, to the import page for a viewer who holds supplier.import.run, otherwise the integrations
// page. Each check belongs to one part:
//
//   connection   erp_connection_test_failed       the last connection test failed
//                erp_address_not_https            the address in force is plain http; no usable address is not
//                                                 an alert here, since then nothing is being sent anywhere
//   sync         erp_sync_failed                  the last hourly sync failed and the connection is enabled;
//                                                 a failure from before it was switched off is not an alert
//                erp_sync_stale                   the hourly sync is stale
//                erp_import_unfinished            an import started and recorded no outcome
//   push         erp_push_host_not_on_write_hosts the switch is on and the server in use is not on Erp:WriteHosts
//                erp_push_no_default_group        the switch is on and no supplier group is set
//                erp_push_failed_or_stalled       the switch is on and pushes failed or stalled
//
// Every push check fires only while the push switch is on (Q10). While it is off the push writes nothing, the
// suppliers it would create are waiting rather than late, and the dashboard shows that as information, never as
// an item here.
//
// Failed and stalled pushes are one item rather than two. The ERP section gives one list of reference codes for
// both, oldest request first, and cannot say which code is which; split in two, each item would carry codes that
// belong to the other. The count is the two figures added, which is a count of suppliers, because a failed push
// is not one the push works on and so is never also stalled. The ERP block beside it shows them apart. Each code
// links to /back-office/review/{code} for a viewer who holds both admin.integrations.manage, which the ERP
// section is hidden without, and supplier.review, which that page is gated on; for anyone else the code is shown
// with no link.
//
// From people and access. The staff list is a list of staff only, so the staff figures link to it and the
// supplier figures have no link: no page an administrator holds lists supplier logins with their lockouts or
// their second factor. They are still raised, because they are still accounts that cannot get in.
//
//   staff_locked_out                staff accounts whose lockout has not run out        /back-office/staff
//   staff_invitations_lapsed        staff invitations whose links all expired unused    /back-office/staff
//   staff_cannot_sign_in            staff who must use two-factor and have none          /back-office/staff
//   supplier_logins_locked_out      supplier logins whose lockout has not run out       no link
//   supplier_logins_cannot_sign_in  supplier logins who must use two-factor and have none no link
//
// "Cannot sign in" is never paired with a suggestion to reset two-factor (Q13): resetting it changes nothing for
// somebody who has none, and the key says what is wrong rather than what to press.
//
// From security, to the audit search:
//
//   security_spike              security events that are spiking, counted as events, by DashboardSecuritySpike
//
// Counted by the section itself, to the review queue:
//
//   review_deadline_passed      applications in Submitted or UnderReview past their review target (Q2, A-5)

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Integration;

public static class DashboardNeedsAttentionChecks
{
    public const string JobsNeedAttention = "jobs_need_attention";
    public const string NoLiveJobServers = "no_live_job_servers";
    public const string EmailsFailed = "emails_failed";
    public const string OutboxStuck = "outbox_stuck";
    public const string OutboxFailed = "outbox_failed";
    public const string PurchaseOrderSendsFailed = "purchase_order_sends_failed";
    public const string ScansStuck = "scans_stuck";
    public const string MigrationsPending = "migrations_pending";
    public const string ObjectStorageUnreachable = "object_storage_unreachable";
    public const string ReferenceListsEmpty = "reference_lists_empty";

    public const string ErpConnectionTestFailed = "erp_connection_test_failed";
    public const string ErpAddressNotHttps = "erp_address_not_https";

    public const string ErpSyncFailed = "erp_sync_failed";
    public const string ErpSyncStale = "erp_sync_stale";
    public const string ErpImportUnfinished = "erp_import_unfinished";

    public const string ErpPushHostNotOnWriteHosts = "erp_push_host_not_on_write_hosts";
    public const string ErpPushNoDefaultGroup = "erp_push_no_default_group";
    public const string ErpPushFailedOrStalled = "erp_push_failed_or_stalled";

    public const string StaffLockedOut = "staff_locked_out";
    public const string StaffInvitationsLapsed = "staff_invitations_lapsed";
    public const string StaffCannotSignIn = "staff_cannot_sign_in";
    public const string SupplierLoginsLockedOut = "supplier_logins_locked_out";
    public const string SupplierLoginsCannotSignIn = "supplier_logins_cannot_sign_in";

    public const string SecuritySpike = "security_spike";

    public const string ReviewDeadlinePassed = "review_deadline_passed";

    public static readonly IReadOnlyList<string> SystemHealth =
    [
        JobsNeedAttention, NoLiveJobServers, EmailsFailed, OutboxStuck, OutboxFailed, PurchaseOrderSendsFailed,
        ScansStuck, MigrationsPending, ObjectStorageUnreachable, ReferenceListsEmpty,
    ];

    public static readonly IReadOnlyList<string> ErpConnection = [ErpConnectionTestFailed, ErpAddressNotHttps];

    public static readonly IReadOnlyList<string> ErpSync = [ErpSyncFailed, ErpSyncStale, ErpImportUnfinished];

    public static readonly IReadOnlyList<string> ErpPush =
        [ErpPushHostNotOnWriteHosts, ErpPushNoDefaultGroup, ErpPushFailedOrStalled];

    public static readonly IReadOnlyList<string> PeopleAndAccess =
    [
        StaffLockedOut, StaffInvitationsLapsed, StaffCannotSignIn, SupplierLoginsLockedOut, SupplierLoginsCannotSignIn,
    ];

    public static readonly IReadOnlyList<string> Security = [SecuritySpike];

    public static readonly IReadOnlyList<string> ReviewDeadline = [ReviewDeadlinePassed];
}

public static class DashboardNeedsAttention
{
    public const string OperationsPage = "/back-office/operations";
    public const string ReferencePage = "/back-office/reference";
    public const string IntegrationsPage = "/back-office/integrations";
    public const string ErpImportPage = "/back-office/erp-import";
    public const string StaffPage = "/back-office/staff";
    public const string AuditPage = "/back-office/audit";
    public const string ReviewQueuePage = "/back-office/review";

    public const int ErpReferenceCodesShown = 5;

    public static string ReviewPage(string referenceCode) =>
        $"{ReviewQueuePage}/{Uri.EscapeDataString(referenceCode)}";

    public static DashboardNeedsAttentionDto Compute(
        DashboardViewer viewer, DashboardSectionResults others, int? overdueReviews)
    {
        var found = new Found(viewer);

        Judge(others.SystemHealth, DashboardNeedsAttentionChecks.SystemHealth, found, SystemHealth);

        switch (others.Erp.Status)
        {
            case DashboardSectionStatus.Failed:
                found.NotRun(DashboardNeedsAttentionChecks.ErpConnection);
                found.NotRun(DashboardNeedsAttentionChecks.ErpSync);
                found.NotRun(DashboardNeedsAttentionChecks.ErpPush);
                break;
            case DashboardSectionStatus.Ok:
                var erp = others.Erp.Data!;
                Judge(erp.Connection, DashboardNeedsAttentionChecks.ErpConnection, found, ErpConnection);
                Judge(erp.Sync, DashboardNeedsAttentionChecks.ErpSync, found, ErpSync);
                Judge(erp.Push, DashboardNeedsAttentionChecks.ErpPush, found, ErpPush);
                break;
        }

        Judge(others.PeopleAndAccess, DashboardNeedsAttentionChecks.PeopleAndAccess, found, PeopleAndAccess);
        Judge(others.Security, DashboardNeedsAttentionChecks.Security, found, Security);

        if (overdueReviews is null)
        {
            found.NotRun(DashboardNeedsAttentionChecks.ReviewDeadline);
        }
        else if (overdueReviews > 0)
        {
            found.Add(
                DashboardNeedsAttentionChecks.ReviewDeadlinePassed,
                overdueReviews,
                found.LinkFor(Permissions.SupplierReview, ReviewQueuePage));
        }

        return new DashboardNeedsAttentionDto(
            found.Items.Count == 0 && found.ChecksNotRun.Count == 0,
            found.Items,
            found.ChecksNotRun);
    }

    private static void Judge<TData>(
        DashboardSection<TData> section, IReadOnlyList<string> checks, Found found, Action<TData, Found> judge)
        where TData : class
    {
        switch (section.Status)
        {
            case DashboardSectionStatus.Failed:
                found.NotRun(checks);
                break;
            case DashboardSectionStatus.Ok:
                judge(section.Data!, found);
                break;
        }
    }

    private static void SystemHealth(DashboardSystemHealthDto health, Found found)
    {
        var operations = found.LinkFor(Permissions.AdminUsersManage, OperationsPage);

        var jobs = health.Jobs.Jobs.Count(j => j.Verdict is
            DashboardJobVerdict.Late or DashboardJobVerdict.Failed or DashboardJobVerdict.Retrying
            or DashboardJobVerdict.Missing);
        found.AddCount(DashboardNeedsAttentionChecks.JobsNeedAttention, jobs, operations);

        if (health.Queue.LiveServers == 0)
        {
            found.Add(DashboardNeedsAttentionChecks.NoLiveJobServers, null, operations);
        }

        found.AddCount(DashboardNeedsAttentionChecks.EmailsFailed, health.Email.FailedInWindow, operations);
        found.AddCount(DashboardNeedsAttentionChecks.OutboxStuck, health.Outbox.Stuck, operations);
        found.AddCount(DashboardNeedsAttentionChecks.OutboxFailed, health.Outbox.Failed, operations);
        found.AddCount(
            DashboardNeedsAttentionChecks.PurchaseOrderSendsFailed,
            health.PurchaseOrderTransport.FailedSends,
            operations);
        found.AddCount(DashboardNeedsAttentionChecks.ScansStuck, health.Scans.Stuck, operations);
        found.AddCount(DashboardNeedsAttentionChecks.MigrationsPending, health.PendingMigrations.Count, null);

        if (!health.ObjectStorage.Reachable)
        {
            found.Add(DashboardNeedsAttentionChecks.ObjectStorageUnreachable, null, operations);
        }

        found.AddCount(
            DashboardNeedsAttentionChecks.ReferenceListsEmpty,
            health.ReferenceLists.Count(l => l.Active == 0),
            found.LinkFor(Permissions.ReferenceDataManage, ReferencePage));
    }

    private static void ErpConnection(DashboardErpConnectionDto connection, Found found)
    {
        if (connection.LastTestSucceeded == false)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpConnectionTestFailed, null, found.ErpLink);
        }

        if (connection.Https == false)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpAddressNotHttps, null, found.ErpLink);
        }
    }

    private static void ErpSync(DashboardErpSyncDto sync, Found found)
    {
        if (sync.Enabled && sync.Outcome == IntegrationSyncOutcome.Failed)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpSyncFailed, null, found.ErpLink);
        }

        if (sync.Stale)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpSyncStale, null, found.ErpLink);
        }

        if (sync.UnfinishedRunStartedAt is not null)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpImportUnfinished, null, found.ErpLink);
        }
    }

    private static void ErpPush(DashboardErpPushDto push, Found found)
    {
        if (!push.SwitchOn)
        {
            return;
        }

        if (!push.HostOnWriteHosts)
        {
            found.Add(DashboardNeedsAttentionChecks.ErpPushHostNotOnWriteHosts, null, found.ErpLink);
        }

        if (string.IsNullOrWhiteSpace(push.DefaultGroup))
        {
            found.Add(DashboardNeedsAttentionChecks.ErpPushNoDefaultGroup, null, found.ErpLink);
        }

        var stuck = push.Failed + (push.Stalled ?? 0);
        if (stuck > 0)
        {
            var linked = found.Viewer.HasPermission(Permissions.AdminIntegrationsManage)
                && found.Viewer.HasPermission(Permissions.SupplierReview);
            var references = push.ReferenceCodes
                .Take(ErpReferenceCodesShown)
                .Select(code => new DashboardAttentionReferenceDto(code, linked ? ReviewPage(code) : null))
                .ToList();

            found.Add(DashboardNeedsAttentionChecks.ErpPushFailedOrStalled, stuck, found.ErpLink, references);
        }
    }

    private static void PeopleAndAccess(DashboardPeopleAndAccessDto people, Found found)
    {
        var figures = people.NeedsAttention;
        var staff = found.LinkFor(Permissions.AdminUsersManage, StaffPage);

        found.AddCount(DashboardNeedsAttentionChecks.StaffLockedOut, figures.LockedOutStaff, staff);
        found.AddCount(DashboardNeedsAttentionChecks.StaffInvitationsLapsed, figures.LapsedStaffInvitations, staff);
        found.AddCount(DashboardNeedsAttentionChecks.StaffCannotSignIn, figures.StaffWhoCannotSignIn, staff);
        found.AddCount(DashboardNeedsAttentionChecks.SupplierLoginsLockedOut, figures.LockedOutSuppliers, null);
        found.AddCount(
            DashboardNeedsAttentionChecks.SupplierLoginsCannotSignIn, figures.SuppliersWhoCannotSignIn, null);
    }

    private static void Security(DashboardSecurityDto security, Found found) =>
        found.AddCount(
            DashboardNeedsAttentionChecks.SecuritySpike,
            security.Events.Count(e => e.Spiking),
            found.LinkFor(Permissions.AuditRead, AuditPage));

    // What has been found so far, in the order the checks are listed above, and the viewer the links are for.
    private sealed class Found(DashboardViewer viewer)
    {
        public DashboardViewer Viewer { get; } = viewer;

        public List<DashboardAttentionItemDto> Items { get; } = [];

        public List<string> ChecksNotRun { get; } = [];

        public string? ErpLink =>
            LinkFor(Permissions.SupplierImportRun, ErpImportPage)
            ?? LinkFor(Permissions.AdminIntegrationsManage, IntegrationsPage);

        public string? LinkFor(string permission, string page) => Viewer.HasPermission(permission) ? page : null;

        public void NotRun(IReadOnlyList<string> checks) => ChecksNotRun.AddRange(checks);

        public void Add(
            string key, int? count, string? link, IReadOnlyList<DashboardAttentionReferenceDto>? references = null) =>
            Items.Add(new DashboardAttentionItemDto(key, count, link, references ?? []));

        public void AddCount(string key, int count, string? link)
        {
            if (count > 0)
            {
                Add(key, count, link);
            }
        }
    }
}

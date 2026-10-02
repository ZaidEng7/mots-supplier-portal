// The ERP section of the administrator's dashboard: the connection, the hourly sync and the supplier push, read from
// the database and configuration only. It is hidden from a viewer without admin.integrations.manage.
//
// NOTHING HERE CAME FROM THE ERP. Every figure is a row the import, the push or the connection test already wrote, or a
// setting the deployment already holds. The screen refreshes every minute in every open tab, and a dashboard that asked
// the ERP would send it a request a minute per administrator; the owner's rule is that the dashboard never does.
//
//
// THE SYNC AND THE PUSH CAN FAIL ON THEIR OWN
//
// Each carries the frame's own envelope, DashboardSection: ok with its data, or failed with none. Hidden never appears
// inside this section, because the whole block is hidden or none of it is.
//
// They fail on their own because their columns arrived in migrations that some environments apply by hand: the sync's
// in NightlyErpSync (#230), the push's in NightlyErpSync and ErpSupplierPush (#232). An environment missing one would
// otherwise lose the whole ERP block, or the whole answer, over a column. The failure is logged on the server, and the
// answer says only that the part could not be read.
//
// The connection has no status of its own. It is read from columns the table has had since it was created, and nothing
// else in the section can be worked out without it, so when it cannot be read the section fails as a whole.
//
//
// THE CONNECTION
//
// Source is where the address in force comes from, by the rule ErpConnectionProvider applies: the connection's row once
// somebody has saved an address on the integrations screen, the deployment's Erp settings until then, and None when
// neither has one. Enabled is the switch that goes with that address, the row's or the settings'.
//
// Host is the address's host, with its port when it is not the scheme's default, and never the whole address, the key
// or the secret. Https says whether the address uses it; both are null when there is no address, or one that is not an
// http or https URL. The last test is the one somebody ran from the integrations screen, kept on the row; its detail stays on that
// screen.
//
//
// THE HOURLY SYNC
//
// The last run is the one the connection records, with its time and its outcome, whoever started it. That record is the
// authority: the import writes it at the end of every run, the integrations screen shows it, and it reaches back to
// runs from before the audit trail recorded how a run ended. Trigger and Counts come from the run's closing row on the
// trail, ErpImportCompleted or ErpImportFailed, which the import writes in the same save. A run that threw has no
// counts, and a run from before the trail recorded endings has neither.
//
// Stale means the connection is enabled and no run has recorded an outcome in the last three hours, which includes one
// that never has. A disabled connection is never stale: it is not meant to sync.
//
// UnfinishedRunStartedAt is the start of an import that began more than thirty minutes ago and has recorded no outcome
// since: no closing row of its own, and nothing recorded on the connection after it started. Imports never overlap, so
// that is an import that hung or died without saying how. The earliest such start is given. It is null when there is
// none.
//
//
// THE SUPPLIER PUSH
//
// SwitchOn is the write switch on the connection's row, and DefaultGroup the ERP supplier group every supplier the push
// creates is filed under. HostOnWriteHosts says whether the server in use is listed in Erp:WriteHosts, by the same match
// the writer uses; the list itself stays in the deployment's configuration.
//
// Waiting is the number of approved suppliers the push would create in the ERP, by the push's own rule, Pushable: in
// service, and Requested or Linked. It is the same number the integrations screen asks a person to agree to before
// turning the switch on. While the switch is off it is information, "N approved suppliers waiting", and never an alert.
//
// Failed counts the pushes that stopped and wait for a person to retry them, whatever the switch says.
//
// Stalled is counted only while the switch is on, and is null while it is off, because a push that is switched off is
// waiting rather than late. It counts the suppliers the push works on whose next attempt is more than fifteen minutes
// overdue, or whose in-flight marker was set more than ten minutes ago, which is an attempt that began and never
// recorded how it ended. A stalled supplier is also counted in Waiting.
//
// The reference codes are the first five failed and the first five stalled suppliers, in the order the push takes them,
// oldest request first, so that each can be opened from the dashboard.
//
// No figure here is an amount.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;

public sealed record DashboardErpDto(
    DashboardErpConnectionDto Connection,
    DashboardSection<DashboardErpSyncDto> Sync,
    DashboardSection<DashboardErpPushDto> Push);

public enum DashboardErpSource
{
    None,
    Configuration,
    Database,
}

public sealed record DashboardErpConnectionDto(
    DashboardErpSource Source,
    bool Enabled,
    string? Host,
    bool? Https,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded);

public sealed record DashboardErpSyncDto(
    DateTimeOffset? LastRunAt,
    IntegrationSyncOutcome? Outcome,
    ErpImportTrigger? Trigger,
    DashboardErpSyncCounts? Counts,
    bool Stale,
    DateTimeOffset? UnfinishedRunStartedAt);

public sealed record DashboardErpSyncCounts(
    int ErpSuppliers,
    int Created,
    int Updated,
    int Suspended,
    int Refused,
    int Failed);

public sealed record DashboardErpPushDto(
    bool SwitchOn,
    string? DefaultGroup,
    bool HostOnWriteHosts,
    int Waiting,
    int Failed,
    int? Stalled,
    IReadOnlyList<string> FailedReferenceCodes,
    IReadOnlyList<string> StalledReferenceCodes);

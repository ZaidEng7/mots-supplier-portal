// The ERP section of the administrator's dashboard: the connection, the hourly sync and the supplier push, read from
// the database and configuration only. It is hidden from a viewer without admin.integrations.manage.
//
// NOTHING HERE CAME FROM THE ERP. Every figure is a row the import, the push or the connection test already wrote, or a
// setting the deployment already holds. The screen refreshes every minute in every open tab, and a dashboard that asked
// the ERP would send it a request a minute per administrator; the owner's rule is that the dashboard never does.
//
//
// THE THREE PARTS FAIL ON THEIR OWN
//
// The connection, the sync and the push each carry the frame's own envelope, DashboardSection: ok with its data, or
// failed with none. Hidden never appears inside this section, because the whole block is hidden or none of it is.
//
// They fail on their own because their columns arrived in migrations that some environments apply by hand: the sync's
// in NightlyErpSync (#230), the push's in NightlyErpSync and ErpSupplierPush (#232). An environment missing one would
// otherwise lose the whole ERP block, or the whole answer, over a column. The connection's columns are older than
// either, and it is kept apart all the same, so that no part depends on another having been read: each part reads the
// address in force for itself. A failure is logged on the server, and the answer says only that the part could not be
// read.
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
// http or https URL. The last test is the one somebody ran from the integrations screen, kept on the row; its detail
// stays on that screen.
//
//
// THE HOURLY SYNC
//
// The last run is the one the connection's row records in its LastSync columns, with its time and its outcome, whoever
// started it. That record is the authority: the import writes it at the end of every run in the same save as the run's
// closing row on the audit trail, ErpImportCompleted or ErpImportFailed, the integrations screen shows it, and it
// reaches back to runs from before the trail recorded how a run ended. Only when the row records no run, as for a
// deployment with no connection row, is the latest closing row on the trail taken instead.
//
// Trigger and Counts come from that run's closing row, which carries them in its details. A run that threw has no
// counts, and a run from before the trail recorded endings has neither.
//
// Stale means the connection is enabled and no run has recorded an outcome in the last three hours, which includes one
// that never has. A disabled connection is never stale: it is not meant to sync.
//
// Enabled is whether the connection in force is enabled, the same judgement Stale uses. Nothing clears the last
// outcome when a connection is switched off, so a failure recorded before that stays the last outcome for good; the
// needs-attention section reads Enabled to leave such a failure out rather than alert on it forever.
//
// UnfinishedRunStartedAt is the start of an import, an ErpImportRun row, that began more than thirty minutes ago and has
// recorded no outcome since: no closing row of its own, and no run recorded after it started. Imports never overlap, so
// that is an import that hung or died without saying how. The earliest such start is given. It is null when there is
// none.
//
//
// THE SUPPLIER PUSH
//
// SwitchOn is the write switch on the connection's row, and DefaultGroup the ERP supplier group every supplier the push
// creates is filed under. HostOnWriteHosts says whether the server in use is listed in Erp:WriteHosts, by the same
// match the writer uses (ErpWriteHosts); it is false when there is no address. The list itself stays in the
// deployment's configuration and is never part of this answer.
//
// Waiting is the number of approved suppliers the push would create in the ERP, by the push's own rule, Pushable: in
// service, and Requested or Linked. It is the same number the integrations screen asks a person to agree to before
// turning the switch on. While the switch is off it is information, "push to ERP is off: N approved suppliers waiting",
// and never an alert or a warning.
//
// Failed counts the pushes that stopped and wait for a person to retry them, whatever the switch says. Like every push
// figure it is information while the switch is off; push alerts fire only while it is on.
//
// Stalled is counted only while the switch is on, and is null while it is off, because a push that is switched off is
// waiting rather than late. It counts the suppliers the push works on whose next attempt is more than fifteen minutes
// overdue, or whose in-flight marker was set more than ten minutes ago, which is an attempt that began and never
// recorded how it ended. A stalled supplier is also counted in Waiting.
//
// ReferenceCodes names up to five suppliers whose push failed or, while the switch is on, stalled, oldest request
// first, so that each can be opened from the dashboard at /back-office/review/{code}.
//
// No figure here is an amount.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;

public sealed record DashboardErpDto(
    DashboardSection<DashboardErpConnectionDto> Connection,
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
    DateTimeOffset? UnfinishedRunStartedAt,
    bool Enabled);

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
    IReadOnlyList<string> ReferenceCodes);

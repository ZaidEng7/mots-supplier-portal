// The administrator's dashboard: GET /api/v1/admin/dashboard, and the stuck-scan retry its virus-scan tile offers.
//
// The server answers every section on its own, as ok, failed or hidden. Ok carries the section's data. Failed means
// the section threw and carries nothing, so the page says that section could not be loaded and the others still
// show. Hidden means the viewer lacks the permission the section is gated on, and the page leaves it out entirely,
// with no heading and no placeholder. The ERP section is split the same way one level down: its connection, hourly
// sync and push each answer ok or failed on their own.
//
// Dates arrive as ISO strings and are formatted on the page. Counts are plain numbers. The enums arrive as the
// server's own names, which the page maps to labels; an enum value with no label is shown as it came rather than
// dropped, so a value added on the server is visible before it is translated.
//
// The storage probe is in admin.ts, beside the operations page that also uses it.
//
// retryStuckScans requeues up to a hundred supplier documents that have waited more than fifteen minutes for the
// scanner, and reports how many it requeued, how many it left for the next press, and the reference codes of the
// documents whose quarantined file was gone and so could not be scanned again.

import { apiFetch } from './auth'

export type DashboardSectionStatus = 'ok' | 'failed' | 'hidden'

export interface DashboardSection<T> {
  status: DashboardSectionStatus
  data: T | null
}

export type DashboardJobVerdict = 'ok' | 'late' | 'failed' | 'retrying' | 'missing' | 'disabled'

export interface DashboardJob {
  id: string
  verdict: DashboardJobVerdict
  lateAfterMinutes: number
  lastState: string | null
  lastExecution: string | null
  nextExecution: string | null
  link: string
}

export interface DashboardSystemHealth {
  jobs: { recurringEnabled: boolean; jobs: DashboardJob[] }
  queue: {
    enqueued: number
    processing: number
    retrying: number
    failed: number
    liveServers: number
    heartbeatWithinMinutes: number
  }
  email: { failedInWindow: number; retrying: number; windowDays: number }
  outbox: { pending: number; stuck: number; stuckAfterMinutes: number; failed: number; oldestPendingAt: string | null }
  scans: { stuck: number; stuckAfterMinutes: number }
  pendingMigrations: string[]
  referenceLists: { table: string; active: number; inactive: number }[]
  purchaseOrderTransport: { configured: boolean; failedSends: number }
  objectStorage: { reachable: boolean }
}

export interface DashboardErpConnection {
  source: 'None' | 'Configuration' | 'Database'
  enabled: boolean
  host: string | null
  https: boolean | null
  lastTestedAt: string | null
  lastTestSucceeded: boolean | null
}

export interface DashboardErpSync {
  lastRunAt: string | null
  outcome: 'Succeeded' | 'NeedsAttention' | 'Failed' | null
  trigger: string | null
  counts: {
    erpSuppliers: number
    created: number
    updated: number
    suspended: number
    refused: number
    failed: number
  } | null
  stale: boolean
  unfinishedRunStartedAt: string | null
  enabled: boolean
}

export interface DashboardErpPush {
  switchOn: boolean
  defaultGroup: string | null
  hostOnWriteHosts: boolean
  waiting: number
  failed: number
  stalled: number | null
  referenceCodes: string[]
}

export interface DashboardErp {
  connection: DashboardSection<DashboardErpConnection>
  sync: DashboardSection<DashboardErpSync>
  push: DashboardSection<DashboardErpPush>
}

export interface DashboardAccounts {
  active: number
  inactive: number
  activeWithARole: number
  activeSessions: number
  peopleWithActiveSessions: number
  pendingInvitations: number
  lockedOut: number
  cannotSignIn: number
}

export interface DashboardPeopleAndAccess {
  staff: DashboardAccounts
  suppliers: DashboardAccounts
  activeUsersByRole: { role: string; activeUsers: number }[]
  staffInvitedNeverSignedIn: { linkStillValid: number; linkExpired: number; noLinkYet: number; linkUsed: number }
  twoFactorRequiredRoles: string[]
  supplierLoginsOnPlaceholderAddresses: number
  organisationsByType: { type: string; active: number; inactive: number }[]
}

export interface DashboardAuditRow {
  id: string
  occurredAt: string
  action: string
  actorKind: 'User' | 'System' | 'Integration'
  actorName: string
  aggregateType: string
  referenceCode: string | null
}

export interface DashboardSecurity {
  countedSince: string | null
  events: { action: string; last24Hours: number; last7Days: number; spiking: boolean }[]
  sensitiveChanges: DashboardAuditRow[]
}

export interface DashboardRecentActivity {
  last24Hours: number
  systemLast24Hours: number
  latest: DashboardAuditRow[]
}

export interface DashboardAttentionItem {
  key: string
  count: number | null
  link: string | null
  references: { code: string; link: string | null }[]
}

export interface DashboardNeedsAttention {
  allClear: boolean
  items: DashboardAttentionItem[]
  checksNotRun: string[]
}

export interface AdminDashboard {
  generatedAt: string
  systemHealth: DashboardSection<DashboardSystemHealth>
  erp: DashboardSection<DashboardErp>
  peopleAndAccess: DashboardSection<DashboardPeopleAndAccess>
  security: DashboardSection<DashboardSecurity>
  recentActivity: DashboardSection<DashboardRecentActivity>
  needsAttention: DashboardSection<DashboardNeedsAttention>
}

export async function getAdminDashboard(): Promise<AdminDashboard> {
  const response = await apiFetch('/api/v1/admin/dashboard')
  if (!response.ok) throw new Error('admin_dashboard_unavailable')
  return (await response.json()) as AdminDashboard
}

export interface StuckScanRetry {
  requeued: number
  stillPending: number
  quarantineFileMissing: string[]
}

export async function retryStuckScans(): Promise<StuckScanRetry> {
  const response = await apiFetch('/api/v1/admin/scans/retry', { method: 'POST' })
  if (!response.ok) throw new Error('stuck_scan_retry_failed')
  return (await response.json()) as StuckScanRetry
}

// The needs-attention checks the server can raise, grouped by the part of the dashboard each one is judged on, as
// DashboardNeedsAttentionChecks lists them. The page labels each item by its key and names, for a check that could
// not run, the part it belongs to. The i18n coverage sweep reads both lists, so a check added here without a label
// fails there instead of printing its key on the screen.
const DASHBOARD_ATTENTION_CHECKS = {
  systemHealth: [
    'jobs_need_attention', 'no_live_job_servers', 'emails_failed', 'outbox_stuck', 'outbox_failed',
    'purchase_order_sends_failed', 'scans_stuck', 'migrations_pending', 'object_storage_unreachable',
    'reference_lists_empty',
  ],
  erp: [
    'erp_connection_test_failed', 'erp_address_not_https', 'erp_sync_failed', 'erp_sync_stale',
    'erp_import_unfinished', 'erp_push_host_not_on_write_hosts', 'erp_push_no_default_group',
    'erp_push_failed_or_stalled',
  ],
  peopleAndAccess: [
    'staff_locked_out', 'staff_invitations_lapsed', 'staff_cannot_sign_in', 'supplier_logins_locked_out',
    'supplier_logins_cannot_sign_in',
  ],
  security: ['security_spike'],
  reviewDeadline: ['review_deadline_passed'],
} as const

export type DashboardAttentionGroup = keyof typeof DASHBOARD_ATTENTION_CHECKS

export const DASHBOARD_ATTENTION_GROUPS = Object.keys(DASHBOARD_ATTENTION_CHECKS) as DashboardAttentionGroup[]

export const DASHBOARD_ATTENTION_KEYS: readonly string[] = DASHBOARD_ATTENTION_GROUPS.flatMap(
  (group) => DASHBOARD_ATTENTION_CHECKS[group],
)

export function attentionGroupOf(check: string): DashboardAttentionGroup | undefined {
  return DASHBOARD_ATTENTION_GROUPS.find((group) => (DASHBOARD_ATTENTION_CHECKS[group] as readonly string[]).includes(check))
}

// The eight recurring jobs the system health section judges, in the order the server lists them.
export const DASHBOARD_JOB_IDS = [
  'document-expiry-lifecycle', 'draft-registration-cleanup', 'outbox-dispatch', 'rfq-timeline', 'award-erp-sync',
  'idempotency-cleanup', 'erp-supplier-sync', 'supplier-erp-push',
] as const

export const DASHBOARD_JOB_VERDICTS: readonly DashboardJobVerdict[] = ['ok', 'late', 'failed', 'retrying', 'missing', 'disabled']

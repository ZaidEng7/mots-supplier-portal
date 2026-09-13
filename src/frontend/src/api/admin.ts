// FR-DSH-006 and SCR-700's platform-administration reads: the overview, the jobs monitor, the outbox, the ERP
// sync, the security posture and the storage settings.
//
// Operational health, not procurement data: nothing here identifies an RFQ, a proposal or a supplier, because
// system_admin administers the platform and does not evaluate.
//
// THE OVERVIEW. The oldest pending outbox age is null when nothing is pending, and null is not zero: an empty
// queue and a queue whose head arrived this second are different facts, and only the second one can be stuck.
// The ERP flag is B-1 and BRULE-011's - false when the logging stand-in is registered rather than a real
// transport. Without it the tile is an artifact asserting something untrue, because a draining outbox reads as
// "the integration is working" while nothing has left the building.
//
// THE JOBS MONITOR is SCR-721: one recurring job as an operator needs to see it. The fault flag is false when
// this application expects the job and Hangfire does not hold it - the operational fault the overview tile
// counts, carried per row so it is obvious which one. The last state is Hangfire's own vocabulary - "Succeeded",
// "Failed" and the rest - not remapped, because a mapping of ours would hide a state nobody anticipated; it is
// null when the job has never run. triggerRecurringJob is SCR-721's one action, and a 404 from it means Hangfire
// does not hold the job rather than that the run failed.
//
// THE OUTBOX is SCR-722, and it carries every status including the zeroes: "Failed: 0" and a count that failed to
// load must not look alike. Only a Failed message replays, so a 404 there means there was nothing to replay -
// see the endpoint.
//
// THE ERP SYNC is SCR-723, keyed by RFQ code because an Award has no code of its own, and because that is what
// the existing retry endpoint takes. The external reference is null until the ERP acknowledges: a Synced row with
// no reference would mean the adapter reported success without returning anything, which is worth being able to
// see. The transport flag is BRULE-011's again - false when the logging stand-in is registered, in which case
// every row below it is the stub talking to itself.
//
// THE SECURITY POSTURE is SCR-726, read-only by design - see the endpoint - and policy numbers only, no secrets.
// The composition requirement is false by design: SECURITY-ARCHITECTURE §1.4 follows NIST 800-63B, which prefers
// length over composition.
//
// THE STORAGE SETTINGS are SCR-725, read-only by design too, because the upload cap and the allow-list are a
// security control. The allow-list maps an extension to the content type its magic bytes must match, and the
// PAIRING is the rule. The reachability figure is probed when the request was served rather than read from a
// cached health snapshot, and the backlog is there because a backlog that never drains is the failure this screen
// exists to show.

import { apiFetch } from './auth'

export interface AdminOverview {
  usersByRole: { role: string; count: number }[]
  totalRoles: number
  referenceData: { table: string; active: number; inactive: number }[]
  outbox: {
    pending: number
    failed: number
    oldestPendingAgeMinutes: number | null
    erpTransportConfigured: boolean
  }
  jobs: {
    recurringJobsEnabled: boolean
    expectedJobs: string[]
    registeredJobs: string[]
    missingJobs: string[]
  }
  auditRowsLast24Hours: number
}

export async function getAdminOverview(): Promise<AdminOverview> {
  const response = await apiFetch('/api/v1/admin/overview')
  if (!response.ok) throw new Error('admin_overview_unavailable')
  return (await response.json()) as AdminOverview
}

export interface RecurringJobRow {
  id: string
  registered: boolean
  cron: string | null
  lastExecution: string | null
  nextExecution: string | null
  lastState: string | null
}

export interface JobsMonitor {
  recurringEnabled: boolean
  jobs: RecurringJobRow[]
}

export async function getJobsMonitor(): Promise<JobsMonitor> {
  const response = await apiFetch('/api/v1/admin/jobs')
  if (!response.ok) throw new Error('jobs_monitor_unavailable')
  return (await response.json()) as JobsMonitor
}

export async function triggerRecurringJob(jobId: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/jobs/${encodeURIComponent(jobId)}/trigger`, { method: 'POST' })
  if (!response.ok) throw new Error(response.status === 404 ? 'job_not_registered' : 'job_trigger_failed')
}

export interface OutboxMessageRow {
  id: string
  type: string
  syncStatus: string
  createdAt: string
  processedAt: string | null
  payloadJson: string
}

export interface OutboxMonitor {
  counts: Record<string, number>
  messages: OutboxMessageRow[]
}

export async function getOutboxMonitor(status?: string): Promise<OutboxMonitor> {
  const query = status ? `?status=${encodeURIComponent(status)}` : ''
  const response = await apiFetch(`/api/v1/admin/outbox${query}`)
  if (!response.ok) throw new Error('outbox_monitor_unavailable')
  return (await response.json()) as OutboxMonitor
}

export async function replayOutboxMessage(id: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/outbox/${id}/replay`, { method: 'POST' })
  if (!response.ok) throw new Error('outbox_replay_failed')
}

export interface ErpSyncRow {
  rfqReferenceCode: string
  erpSyncStatus: string
  erpRetryCount: number
  erpSyncedAt: string | null
  externalPurchaseOrderRef: string | null
}

export interface ErpSyncMonitor {
  transportConfigured: boolean
  counts: Record<string, number>
  awards: ErpSyncRow[]
}

export async function getErpSyncMonitor(status?: string): Promise<ErpSyncMonitor> {
  const query = status ? `?status=${encodeURIComponent(status)}` : ''
  const response = await apiFetch(`/api/v1/admin/erp-sync${query}`)
  if (!response.ok) throw new Error('erp_sync_monitor_unavailable')
  return (await response.json()) as ErpSyncMonitor
}

export interface SecurityPosture {
  password: {
    minimumLength: number
    requireDigit: boolean
    requireUppercase: boolean
    requireLowercase: boolean
    requireNonAlphanumeric: boolean
  }
  lockout: { maxFailedAttempts: number; lockoutMinutes: number }
  session: { accessTokenMinutes: number; refreshTokenDays: number; clockSkewSeconds: number }
  mfaRequiredRoles: string[]
  rateLimits: { policy: string; permitLimit: number; windowSeconds: number }[]
  registrationMode: string
}

export async function getSecurityPosture(): Promise<SecurityPosture> {
  const response = await apiFetch('/api/v1/admin/security')
  if (!response.ok) throw new Error('security_posture_unavailable')
  return (await response.json()) as SecurityPosture
}

export interface StorageSettings {
  maxUploadBytes: number
  allowedTypes: Record<string, string>
  bucket: string
  objectStorageReachable: boolean
  virusScannerReachable: boolean
  documentCount: number
  pendingScanCount: number
}

export async function getStorageSettings(): Promise<StorageSettings> {
  const response = await apiFetch('/api/v1/admin/storage')
  if (!response.ok) throw new Error('storage_settings_unavailable')
  return (await response.json()) as StorageSettings
}

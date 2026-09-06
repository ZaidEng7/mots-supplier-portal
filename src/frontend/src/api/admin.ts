import { apiFetch } from './auth'

/** FR-DSH-006/SCR-700. Operational health, not procurement data: nothing here identifies an RFQ,
 * a proposal or a supplier, because `system_admin` administers the platform and does not evaluate. */
export interface AdminOverview {
  usersByRole: { role: string; count: number }[]
  totalRoles: number
  referenceData: { table: string; active: number; inactive: number }[]
  outbox: {
    pending: number
    failed: number
    /** Null when nothing is pending. Null is not zero: an empty queue and a queue whose head arrived
     * this second are different facts, and only the second one can be stuck. */
    oldestPendingAgeMinutes: number | null
    /** B-1/BRULE-011: false when the logging stand-in is registered rather than a real ERP transport.
     * Without it the tile is an artifact asserting something untrue - a draining outbox reads as "the
     * integration is working" while nothing has left the building. */
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

/** SCR-721. One recurring job as an operator needs to see it. */
export interface RecurringJobRow {
  id: string
  /** False when this application expects the job and Hangfire does not hold it — the operational fault
   *  the overview tile counts, carried per row so it is obvious which one. */
  registered: boolean
  cron: string | null
  lastExecution: string | null
  nextExecution: string | null
  /** Hangfire's own vocabulary ("Succeeded", "Failed", ...), not remapped: a mapping of ours would hide
   *  a state nobody anticipated. Null when the job has never run. */
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

/** SCR-721's one action. 404 means Hangfire does not hold the job — not that the run failed. */
export async function triggerRecurringJob(jobId: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/jobs/${encodeURIComponent(jobId)}/trigger`, { method: 'POST' })
  if (!response.ok) throw new Error(response.status === 404 ? 'job_not_registered' : 'job_trigger_failed')
}

/** SCR-722. */
export interface OutboxMessageRow {
  id: string
  type: string
  syncStatus: string
  createdAt: string
  processedAt: string | null
  payloadJson: string
}

export interface OutboxMonitor {
  /** Every status including the zeroes: "Failed: 0" and a count that failed to load must not look alike. */
  counts: Record<string, number>
  messages: OutboxMessageRow[]
}

export async function getOutboxMonitor(status?: string): Promise<OutboxMonitor> {
  const query = status ? `?status=${encodeURIComponent(status)}` : ''
  const response = await apiFetch(`/api/v1/admin/outbox${query}`)
  if (!response.ok) throw new Error('outbox_monitor_unavailable')
  return (await response.json()) as OutboxMonitor
}

/** Only a Failed message replays. A 404 here means there was nothing to replay — see the endpoint. */
export async function replayOutboxMessage(id: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/outbox/${id}/replay`, { method: 'POST' })
  if (!response.ok) throw new Error('outbox_replay_failed')
}

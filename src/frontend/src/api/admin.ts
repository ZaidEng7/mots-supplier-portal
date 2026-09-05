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

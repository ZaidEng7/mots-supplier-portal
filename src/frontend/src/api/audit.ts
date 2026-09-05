import { apiFetch } from './auth'

/** B-1/FR-AUD-003: one row of the supplier's own activity trail. */
export interface AuditEntry {
  id: string
  occurredAt: string
  aggregateType: string
  aggregateId: string
  action: string
  fromState: string | null
  toState: string | null
  actorLabel: string | null
}

/**
 * The supplier's own trail.
 *
 * <p>`GET /suppliers/me/audit` and its CSV export have existed since EPIC-01 and NOTHING called either -
 * a compliance affordance that shipped unreachable, which is what the phase 12a sweep found. Strictly
 * reverse-chronological: the endpoint whitelists no other order.</p>
 */
export async function listOwnAuditTrail(cursor?: string): Promise<{
  data: AuditEntry[]
  pagination: { hasMore: boolean; nextCursor: string | null }
}> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  const response = await apiFetch(`/api/v1/suppliers/me/audit${query}`)
  if (!response.ok) throw new Error('audit_unavailable')
  return (await response.json()) as { data: AuditEntry[]; pagination: { hasMore: boolean; nextCursor: string | null } }
}

/**
 * Fetches the CSV export and hands it to the browser.
 *
 * <p>Fetched rather than linked, because the export needs the Authorization header - a plain anchor would
 * arrive unauthenticated and answer 401, which is why an export that "exists" was never reachable from a
 * screen. The blob is revoked immediately after the click: an object URL left alive keeps the whole file
 * in memory for the life of the document.</p>
 */
export async function downloadOwnAuditTrail(): Promise<void> {
  const response = await apiFetch('/api/v1/suppliers/me/audit/export')
  if (!response.ok) throw new Error('audit_export_failed')

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = 'my-activity-trail.csv'
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}

/**
 * T-079/SCR-720: the STAFF audit explorer, behind `audit.read`.
 *
 * <p>Separate from the supplier's own trail above, and deliberately so: that one takes no filters and
 * needs none — a supplier's trail is bounded by being theirs. This one is the whole platform's, so the
 * filters are the screen, and every one of them is applied server-side. `MSP-75` refuses an
 * unrecognised value with a 422 naming the field rather than answering with an unfiltered list, which
 * is the failure a client-side filter would silently reintroduce.</p>
 */
export interface AuditSearchFilters {
  aggregateType?: string
  aggregateId?: string
  actorUserId?: string
  action?: string
  from?: string
  to?: string
}

export interface AuditPage {
  data: AuditEntry[]
  pagination: { hasMore: boolean; nextCursor: string | null; totalCount: number | null }
  meta: { filtersApplied: string[] | null }
}

/** §7's code, carried so the screen can say WHICH filter the server refused. */
export class AuditApiError extends Error {
  status: number
  code?: string
  field?: string

  constructor(status: number, body: unknown) {
    const problem = body as { detail?: string; title?: string; code?: string; errors?: { field?: string }[] } | null
    super(problem?.detail ?? problem?.title ?? `Request failed: ${status}`)
    this.status = status
    this.code = problem?.code
    this.field = problem?.errors?.[0]?.field
  }
}

function auditQuery(filters: AuditSearchFilters, cursor?: string): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (value !== undefined && value !== '') params.set(key, value)
  }
  if (cursor) params.set('cursor', cursor)
  return params.size > 0 ? `?${params}` : ''
}

export async function searchAuditLog(filters: AuditSearchFilters, cursor?: string): Promise<AuditPage> {
  const response = await apiFetch(`/api/v1/audit${auditQuery(filters, cursor)}`)
  const text = await response.text()
  const body = text ? JSON.parse(text) : null
  if (!response.ok) throw new AuditApiError(response.status, body)
  return body as AuditPage
}

/**
 * The filtered export. Same filters, no page limit — an export is "everything the filter matches".
 *
 * <p>Fetched rather than linked, for the same reason the supplier's is: it needs the Authorization
 * header, and a plain anchor would arrive unauthenticated and answer 401.</p>
 */
export async function downloadAuditLog(filters: AuditSearchFilters): Promise<void> {
  const response = await apiFetch(`/api/v1/audit/export${auditQuery(filters)}`)
  if (!response.ok) throw new AuditApiError(response.status, null)

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = 'audit-trail.csv'
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}

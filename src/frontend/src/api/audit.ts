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

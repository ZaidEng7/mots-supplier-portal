import type { Notification } from '../api/notifications'
import { useAuthStore } from './authStore'

/**
 * Where a notification's "open" link should land.
 *
 * <p>INFORMATION-ARCHITECTURE.md §2 requires the bell to "deep-link to the source entity". The
 * destination is an <b>SPA route</b>, not an API route - they were converged for the API in the §12
 * work but the SPA's own URL space is separate and was already correct: a supplier's RFQ list is
 * `/rfqs`, a buyer's is `/back-office/rfqs`.</p>
 *
 * <p><b>Persona decides the prefix, not the notification.</b> The same `rfqCode` means "the RFQ you
 * were invited to" for a supplier and "the RFQ you are running" for an officer, and those are two
 * different screens. Reading the persona from the session claim is what keeps one notification type
 * from needing two payloads.</p>
 */
export function notificationRoute(notification: Notification): string | undefined {
  const data = parseData(notification.data)
  const isSupplier = Boolean(useAuthStore.getState().claims?.supplierId)

  // An explicit route in the payload wins - it is on the allow-list precisely so a notification can
  // point somewhere this function has no rule for.
  if (typeof data.route === 'string') return data.route

  const rfqCode = typeof data.rfqCode === 'string' ? data.rfqCode : undefined
  if (rfqCode === undefined) return undefined

  // A proposal notification still lands on the RFQ: a supplier's proposal is reached THROUGH the
  // RFQ in this app's URL space, and there is no standalone proposal route to point at.
  return isSupplier ? `/rfqs/${rfqCode}` : `/back-office/rfqs/${rfqCode}`
}

function parseData(json: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(json) as unknown
    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {}
  } catch {
    // A payload that will not parse is a data problem, not a reason to break the list: the row still
    // has its words, and those are what the reader came for.
    return {}
  }
}

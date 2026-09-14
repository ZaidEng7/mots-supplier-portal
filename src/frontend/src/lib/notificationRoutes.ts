// Where a notification's "open" link should land.
//
// INFORMATION-ARCHITECTURE.md §2 requires the bell to "deep-link to the source entity". The destination is an SPA route
// rather than an API route: the two were converged for the API in the §12 work, but the SPA's own URL space is separate and
// was already correct - a supplier's RFQ list is /rfqs, a buyer's is /back-office/rfqs.
//
// PERSONA DECIDES THE PREFIX, not the notification. The same rfqCode means "the RFQ you were invited to" for a supplier and
// "the RFQ you are running" for an officer, and those are two different screens. Reading the persona from the session claim
// is what keeps one notification type from needing two payloads.
//
// An explicit route in the payload WINS - it is on the allow-list precisely so a notification can point somewhere this
// function has no rule for.
//
// A proposal notification still lands on the RFQ, because a supplier's proposal is reached THROUGH the RFQ in this app's URL
// space and there is no standalone proposal route to point at.
//
// A payload that will not parse is a data problem rather than a reason to break the list: the row still has its words, and
// those are what the reader came for.

import type { Notification } from '../api/notifications'
import { useAuthStore } from './authStore'

export function notificationRoute(notification: Notification): string | undefined {
  const data = parseData(notification.data)
  const isSupplier = Boolean(useAuthStore.getState().claims?.supplierId)

  if (typeof data.route === 'string') return data.route

  const rfqCode = typeof data.rfqCode === 'string' ? data.rfqCode : undefined
  if (rfqCode === undefined) return undefined

  return isSupplier ? `/rfqs/${rfqCode}` : `/back-office/rfqs/${rfqCode}`
}

function parseData(json: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(json) as unknown
    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

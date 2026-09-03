import { beforeEach, describe, expect, it } from 'vitest'
import { notificationRoute } from './notificationRoutes'
import { useAuthStore } from './authStore'
import type { Notification } from '../api/notifications'

function notification(data: Record<string, string>): Notification {
  return {
    id: 'n-1', type: 'rfq.published',
    titleAr: 'عنوان', titleEn: 'Title', bodyAr: 'نص', bodyEn: 'Body',
    data: JSON.stringify(data), createdAt: '2026-09-03T10:00:00Z', readAt: null, isRead: false,
  }
}

describe('notificationRoute', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, claims: null }))

  it('sends a supplier to their own RFQ space', () => {
    // IA §2 deep-links to the source ENTITY, and the entity lives at a different SPA path per
    // persona: a supplier's RFQ list is /rfqs, a buyer's is /back-office/rfqs.
    useAuthStore.setState({ claims: { supplierId: 'sup-1' } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/rfqs/RFQ-2026-000001')
  })

  it('sends back-office staff to the back-office space', () => {
    useAuthStore.setState({ claims: { organizationId: 'org-1' } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/back-office/rfqs/RFQ-2026-000001')
  })

  it('honours an explicit route in the payload', () => {
    // `route` is on the BRULE-091 allow-list precisely so a notification can point somewhere this
    // function has no rule for, without smuggling content through to do it.
    expect(notificationRoute(notification({ route: '/settings' }))).toBe('/settings')
  })

  it('returns nothing when there is nowhere to go', () => {
    // The control: a payload with no routing keys must not produce a broken link.
    expect(notificationRoute(notification({}))).toBeUndefined()
  })

  it('survives a payload that will not parse', () => {
    const broken = { ...notification({}), data: 'not json' }

    expect(notificationRoute(broken)).toBeUndefined()
  })
})

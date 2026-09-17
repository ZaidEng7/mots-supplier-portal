// Where a notification's "open" link lands.
//
// Every fixture carries the same T-037 classification, because it is irrelevant to routing - which is what this file is about -
// and varying it would pretend the distinction matters here.
//
// A supplier goes to their own RFQ space and back-office staff to the back-office one: IA §2 deep-links to the source ENTITY,
// and the entity lives at a different SPA path per persona, because a supplier's RFQ list is /rfqs and a buyer's is
// /back-office/rfqs.
//
// An EVALUATOR is staff and still cannot open the tender: they hold evaluation.score, evaluation.submit and rfq.clarify, the
// tender answers 403 without rfq.read, and "evaluation reopened" carries only the rfqCode - so it opened a refusal with nothing
// on it. They go to their own scoring screen instead. The officer beside them is the control, holding the officer's permissions
// from Roles.DefaultPermissions in src/backend/Domain/Identity/Permissions.cs and still landing on the tender. A session that
// holds both rfq.read and evaluation.score is the second control, because a rule that looked only for evaluation.score would
// send every administrator away from a tender they can open.
//
// An explicit route in the payload is honoured, because `route` is on the BRULE-091 allow-list precisely so a notification can
// point somewhere this function has no rule for, without smuggling content through to do it.
//
// Nothing is returned when there is nowhere to go - the control, so a payload with no routing keys must not produce a broken
// link - and a payload that will not parse is survived.

import { beforeEach, describe, expect, it } from 'vitest'
import { notificationRoute } from './notificationRoutes'
import { useAuthStore } from './authStore'
import type { Notification } from '../api/notifications'

const OFFICER = [
  'rfq.publish', 'offering.search', 'supplier.directory.read', 'rfq.read', 'rfq.create', 'rfq.edit', 'rfq.submit_review',
  'rfq.close', 'rfq.invite', 'clarification.answer', 'rfq.clarify', 'rfq.addendum', 'evaluation.open',
  'evaluation.consolidate', 'comparison.view', 'award.recommend',
]

const EVALUATOR = ['evaluation.score', 'evaluation.submit', 'rfq.clarify']

function notification(data: Record<string, string>): Notification {
  return {
    id: 'n-1', type: 'rfq.published',
    titleAr: 'عنوان', titleEn: 'Title', bodyAr: 'نص', bodyEn: 'Body',
    data: JSON.stringify(data), createdAt: '2026-09-03T10:00:00Z', readAt: null, isRead: false,
    isActionable: true,
  }
}

describe('notificationRoute', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, claims: null }))

  it('sends a supplier to their own RFQ space', () => {
    useAuthStore.setState({ claims: { supplierId: 'sup-1' } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/rfqs/RFQ-2026-000001')
  })

  it('sends back-office staff to the back-office space', () => {
    useAuthStore.setState({ claims: { organizationId: 'org-1', permissions: OFFICER } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/back-office/rfqs/RFQ-2026-000001')
  })

  it('sends an evaluator to their own scoring screen', () => {
    useAuthStore.setState({ claims: { organizationId: 'org-1', permissions: EVALUATOR } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/back-office/rfqs/RFQ-2026-000001/my-evaluation')
  })

  it('keeps a reader who can open the tender on the tender, even when they can also score it', () => {
    useAuthStore.setState({ claims: { organizationId: 'org-1', permissions: [...OFFICER, ...EVALUATOR] } as never })

    expect(notificationRoute(notification({ rfqCode: 'RFQ-2026-000001' }))).toBe('/back-office/rfqs/RFQ-2026-000001')
  })

  it('honours an explicit route in the payload', () => {
    expect(notificationRoute(notification({ route: '/settings' }))).toBe('/settings')
  })

  it('returns nothing when there is nowhere to go', () => {
    expect(notificationRoute(notification({}))).toBeUndefined()
  })

  it('survives a payload that will not parse', () => {
    const broken = { ...notification({}), data: 'not json' }

    expect(notificationRoute(broken)).toBeUndefined()
  })
})

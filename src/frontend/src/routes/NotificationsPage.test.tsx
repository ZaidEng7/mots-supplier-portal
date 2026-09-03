import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { renderPage, mockFetch } from '../test/renderPage'

// Same shape every other page test uses: the real Link needs a router context this render does not
// provide, and the assertions here are about the anchor existing, not about navigation.
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { NotificationsPage } = await import('./NotificationsPage')

function envelope(data: unknown[]) {
  return {
    data,
    pagination: { mode: 'cursor', nextCursor: null, prevCursor: null, pageSize: 25, totalCount: null, hasMore: false },
    meta: { sort: '-createdAt', filtersApplied: null },
  }
}

function notification(overrides: Record<string, unknown> = {}) {
  return {
    id: 'n-1', type: 'rfq.approved',
    titleAr: 'تم اعتماد الطلب', titleEn: 'Your RFQ was approved',
    bodyAr: 'اعتُمد الطلب RFQ-2026-000001.', bodyEn: 'RFQ RFQ-2026-000001 was approved.',
    data: JSON.stringify({ rfqCode: 'RFQ-2026-000001' }),
    createdAt: '2026-09-03T10:00:00Z', readAt: null, isRead: false,
    ...overrides,
  }
}

describe('NotificationsPage (SCR-900)', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('empty: shows the empty state rather than an empty list', async () => {
    restore = mockFetch({ '/api/v1/notifications': envelope([]) })

    renderPage(<NotificationsPage />)

    expect(await screen.findByText('No notifications yet')).toBeInTheDocument()
    expect(screen.getByText('Updates about RFQs, your proposals and awards will appear here.')).toBeInTheDocument()
  })

  it('ok: renders the notification with a link to its source entity', async () => {
    restore = mockFetch({ '/api/v1/notifications': envelope([notification()]) })

    renderPage(<NotificationsPage />)

    expect(await screen.findByText('Your RFQ was approved')).toBeInTheDocument()
    expect(screen.getByText('RFQ RFQ-2026-000001 was approved.')).toBeInTheDocument()

    // IA §2: "deep-links to the source entity". Asserted by its text and its target, since the
    // mocked Link above renders a plain anchor with no href for the role query to match.
    const open = screen.getByText('Open')
    expect(open).toBeInTheDocument()
    expect(open.getAttribute('to')).toBe('/back-office/rfqs/RFQ-2026-000001')
  })

  it('an unread notification offers to be marked read; a read one does not', async () => {
    // The control and the negative in one place: without the read row, "no button" would also pass
    // against a page that never renders the button at all.
    restore = mockFetch({
      '/api/v1/notifications': envelope([
        notification({ id: 'n-unread' }),
        notification({ id: 'n-read', titleEn: 'Older item', isRead: true, readAt: '2026-09-03T11:00:00Z' }),
      ]),
    })

    renderPage(<NotificationsPage />)

    await screen.findByText('Your RFQ was approved')
    expect(screen.getAllByRole('button', { name: 'Mark as read' })).toHaveLength(1)
  })

  it('marking all read calls the endpoint', async () => {
    const calls: string[] = []
    const original = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      if ((init?.method ?? 'GET') === 'POST') {
        calls.push(url)
        return new Response(JSON.stringify({ marked: 1 }), { status: 200 })
      }
      return new Response(JSON.stringify(envelope([notification()])), { status: 200 })
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<NotificationsPage />)

    await screen.findByText('Your RFQ was approved')
    await userEvent.click(screen.getByRole('button', { name: 'Mark all as read' }))

    expect(calls.some((url) => url.endsWith('/api/v1/notifications/read-all'))).toBe(true)
  })
})

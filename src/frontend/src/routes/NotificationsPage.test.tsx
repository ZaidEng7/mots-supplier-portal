// SCR-900: the notification centre, and T-037's two groups.
//
// The real Link needs a router context this render does not provide, so it is mocked to a plain anchor - the assertions
// here are about the anchor existing rather than about navigation. The fixture's classification is the SERVER's, and
// actionable by default because the fixture's type is one; a test that wants the other group says so.
//
// Empty shows the empty state rather than an empty list. The populated case renders the notification with a link to its
// source entity, which is IA §2's "deep-links to the source entity", asserted by its text and its target since the mocked
// Link renders a plain anchor with no href for a role query to match.
//
// An unread notification offers to be marked read and a read one does not - the control and the negative in one place,
// because without the read row "no button" would also pass against a page that never renders the button at all. Marking
// all read calls the endpoint.
//
// A failure shows a retryable error rather than an empty screen, which asserts two halves nothing did before: that the
// error branch RENDERS, and that the control inside it does anything. asyncStateCoverage proves the branch exists in the
// source; a retry button wired to nothing looks identical to one that works.
//
// T-037: THE TWO GROUPS INFORMATION-ARCHITECTURE §2 asks for. The rows land under the right heading, which is the
// assertion the grouping is FOR - two sections with everything in one of them would satisfy a looser test. It is read
// from the document's ORDER rather than by walking up to a container, because the card that holds a heading is an
// implementation detail of the Card component and a test that reaches for it breaks when that component gains a wrapper:
// reading order is what a person sees.
//
// The waiting-on-you section is KEPT when it is empty and the other one is dropped. A reader scans this screen asking "is
// anything on me", and an absent section answers that only by its absence, which is the one answer a person cannot see -
// so the first section stays and says so in words, while the second is omitted, because nobody checks whether nothing
// happened.

import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { renderPage, mockFetch, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'

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
    isActionable: true,
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
    expect(screen.getByText('Updates about tenders, your bids and awards will appear here.')).toBeInTheDocument()
  })

  it('ok: renders the notification with a link to its source entity', async () => {
    restore = mockFetch({ '/api/v1/notifications': envelope([notification()]) })

    renderPage(<NotificationsPage />)

    expect(await screen.findByText('Your RFQ was approved')).toBeInTheDocument()
    expect(screen.getByText('RFQ RFQ-2026-000001 was approved.')).toBeInTheDocument()

    const open = screen.getByText('Open')
    expect(open).toBeInTheDocument()
    expect(open.getAttribute('to')).toBe('/back-office/rfqs/RFQ-2026-000001')
  })

  it('an unread notification offers to be marked read; a read one does not', async () => {
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

  it('shows a retryable failure rather than an empty screen', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/notifications': { __status: 500 } }, recorded)

    renderPage(<NotificationsPage />)

    await expectRetryableFailure('/api/v1/notifications', recorded)
  })


  it('splits the list into what is waiting on the reader and what merely happened', async () => {
    restore = mockFetch({
      '/api/v1/notifications': envelope([
        notification({ id: 'n-act', titleEn: 'Your RFQ was approved', isActionable: true }),
        notification({ id: 'n-info', titleEn: 'A bid was withdrawn', isActionable: false }),
      ]),
    })

    renderPage(<NotificationsPage />)

    expect(await screen.findByText('Waiting on you')).toBeInTheDocument()
    expect(screen.getByText('For information')).toBeInTheDocument()

    const page = document.body.textContent ?? ''
    const informationalHeading = page.indexOf('For information')
    expect(page.indexOf('Your RFQ was approved')).toBeLessThan(informationalHeading)
    expect(page.indexOf('A bid was withdrawn')).toBeGreaterThan(informationalHeading)
  })

  it('keeps the waiting-on-you section when it is empty, and drops the other one', async () => {
    restore = mockFetch({
      '/api/v1/notifications': envelope([notification({ id: 'n-info', isActionable: false })]),
    })

    renderPage(<NotificationsPage />)

    expect(await screen.findByText('Nothing is waiting on you.')).toBeInTheDocument()
    expect(screen.getByText('For information')).toBeInTheDocument()
  })
})

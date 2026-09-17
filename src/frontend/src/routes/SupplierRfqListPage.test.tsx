// FEAT-08.6 and FR-INV-006: this list is itself invitation-scoped server-side, and the page renders whatever
// /api/v1/suppliers/me/rfqs returns without any client-side visibility filtering.
//
// THE T2-32 REGRESSION comes first. Before the fix this page read `rfqsQuery.data ?? []` and went straight to the
// length === 0 branch, so a supplier with invitations was told "No invitations yet" for the whole flight of the request -
// and permanently if it failed. Loading and empty must be distinguishable, which is asserted in both directions. The
// fetch in that test deliberately never settles: that is the only way to observe the pending state without racing the
// resolution.
//
// Invited RFQs are listed with reference, title and the caller's own invitation status, using only the projected
// SupplierRfqListItemDto fields - the list no longer returns the whole aggregate, so a fixture carrying items,
// attachments or clarifications would be lying about the wire.
//
// THE PAGING test is the consumer half of the backend's keyset paging. Before this page used useInfiniteQuery it fetched
// page one and stopped, so a supplier with more than 20 invitations simply never saw the rest - no error, no empty state,
// nothing visibly wrong. It asserts page two is APPENDED rather than swapped in: the page-one row must still be on screen
// after "Load more", because a page that replaced its data instead of accumulating would pass a "row from page two is
// visible" check while losing everything above it.
//
// The last two are the failure. It says the fetch failed rather than "no invitations yet" - T2-32 fixed
// loading-versus-empty and left failure sharing the empty branch, so a supplier whose list could not load was told they
// had no invitations, which is a reason to stop looking - and the Try again button actually tries again.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage, expectRetryableFailure, type RecordedRequest } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { SupplierRfqListPage } = await import('./SupplierRfqListPage')

describe('SupplierRfqListPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the loading skeleton and NOT the empty copy while the query is still pending', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (() => new Promise(() => {})) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByRole('status')).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByText('No invitations yet')).not.toBeInTheDocument()
  })

  it('shows the empty state when invited to nothing', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('No invitations yet')).toBeInTheDocument()
  })

  it('lists invited RFQs with reference, title, and my invitation status', async () => {
    restore = mockFetch({
      '/api/v1/rfqs': listPage([
        {
          rfqCode: 'RFQ-2026-000001', titleAr: 'طلب', titleEn: 'Catering RFQ',
          state: 'Published', invitationStatus: 'Invited', createdAt: '2026-08-30T09:00:00Z', submissionDeadline: null,
        },
      ]),
    })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('RFQ-2026-000001')).toBeInTheDocument()
    expect(screen.getByText('Catering RFQ')).toBeInTheDocument()
    expect(screen.getByText('Invited')).toBeInTheDocument()
  })

  it('appends the next page when Load more is used, keeping the rows already shown', async () => {
    const item = (code: string) => ({
      rfqCode: code, titleAr: 'طلب', titleEn: `RFQ ${code}`,
      state: 'Published', invitationStatus: 'Invited', createdAt: '2026-08-30T09:00:00Z', submissionDeadline: null,
    })
    const original = globalThis.fetch
    globalThis.fetch = ((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      const body = url.includes('cursor=')
        ? listPage([item('RFQ-2026-000002')])
        : listPage([item('RFQ-2026-000001')], { hasMore: true, nextCursor: 'CURSOR-1' })
      return Promise.resolve(new Response(JSON.stringify(body), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      }))
    }) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('RFQ-2026-000001')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Load more' }))

    expect(await screen.findByText('RFQ-2026-000002')).toBeInTheDocument()
    expect(screen.getByText('RFQ-2026-000001')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
  })

  it('says the fetch failed rather than "no invitations yet"', async () => {
    restore = mockFetch({ '/api/v1/rfqs': { __status: 500 } })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not load this. Try again.')
    expect(screen.queryByText(/no invitations/i)).not.toBeInTheDocument()
  })

  it('the Try again button actually tries again', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/rfqs': { __status: 500 } }, recorded)

    renderPage(<SupplierRfqListPage />)

    await expectRetryableFailure('/api/v1/rfqs', recorded)
  })
})

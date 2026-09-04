import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage } from '../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { SupplierRfqListPage } = await import('./SupplierRfqListPage')

/** FEAT-08.6/FR-INV-006: this list is itself invitation-scoped server-side - the page renders
 * whatever /api/v1/suppliers/me/rfqs returns without any client-side visibility filtering. */
describe('SupplierRfqListPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  /**
   * T2-32 regression. Before the fix this page read `rfqsQuery.data ?? []` and went straight to the
   * `length === 0` branch, so a supplier with invitations was told "No invitations yet" for the
   * whole flight of the request - and permanently if it failed. Loading and empty must be
   * distinguishable, which is what this asserts in both directions.
   *
   * The fetch deliberately never settles: that is the only way to observe the pending state
   * without racing the resolution.
   */
  it('shows the loading skeleton and NOT the empty copy while the query is still pending', () => {
    const original = globalThis.fetch
    globalThis.fetch = (() => new Promise(() => {})) as typeof fetch
    restore = () => { globalThis.fetch = original }

    renderPage(<SupplierRfqListPage />)

    expect(screen.getByRole('status')).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByText('No invitations yet')).not.toBeInTheDocument()
  })

  it('shows the empty state when invited to nothing', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<SupplierRfqListPage />)

    expect(await screen.findByText('No invitations yet')).toBeInTheDocument()
  })

  it('lists invited RFQs with reference, title, and my invitation status', async () => {
    // Only the projected `SupplierRfqListItemDto` fields - the list no longer returns the whole
    // aggregate, so a fixture carrying items/attachments/clarifications would be lying about the wire.
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

  /**
   * The consumer half of the backend's keyset paging. Before this page used useInfiniteQuery it
   * fetched page one and stopped, so a supplier with more than 20 invitations simply never saw the
   * rest - no error, no empty state, nothing visibly wrong.
   *
   * <p>Asserts that page two is APPENDED, not swapped in: the page-one row must still be on screen
   * after "Load more". A page that replaced its data instead of accumulating would pass a
   * "row from page two is visible" check while losing everything above it.</p>
   */
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
})

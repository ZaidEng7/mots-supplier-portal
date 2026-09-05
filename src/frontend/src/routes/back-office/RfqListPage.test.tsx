import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage, type RecordedRequest } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { RfqListPage } = await import('./RfqListPage')

// The list endpoint projects `RfqListItemDto` - reference, both titles, state, createdAt, and (A-7)
// the owner's id and name - not the
// full aggregate. The fixture carries exactly those fields, so a page that starts reading something
// the list no longer sends fails here rather than in production.
const RFQ_DRAFT = {
  referenceCode: 'RFQ-2026-000001', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
  state: 'Draft', createdAt: '2026-08-30T09:00:00Z',
  ownerUserId: 'u-officer-1', ownerName: 'An Officer',
}

const RFQ_PUBLISHED = { ...RFQ_DRAFT, referenceCode: 'RFQ-2026-000002', titleEn: 'Published RFQ', state: 'Published' }

/** FEAT-07.1: list + create flow, and that the state badge reflects the real RfqState value
 * (not a client-derived label) so a reviewer can tell lifecycle stage at a glance. */
describe('RfqListPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the empty state when no RFQs exist', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<RfqListPage />)

    expect(await screen.findByText('No RFQs yet')).toBeInTheDocument()
  })

  it('lists RFQs with their reference code, title, and real state badge', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT, RFQ_PUBLISHED]) })

    renderPage(<RfqListPage />)

    const draftRow = (await screen.findByText('RFQ-2026-000001')).closest('tr') as HTMLElement
    expect(within(draftRow).getByText('Sample RFQ')).toBeInTheDocument()
    expect(within(draftRow).getByText('Draft')).toBeInTheDocument()

    const publishedRow = screen.getByText('RFQ-2026-000002').closest('tr') as HTMLElement
    expect(within(publishedRow).getByText('Published')).toBeInTheDocument()
  })

  it('creating an RFQ shows a success toast', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<RfqListPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'New RFQ' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Title (Arabic)', { exact: false }), 'طلب جديد')
    await userEvent.type(within(dialog).getByLabelText('Title (English)', { exact: false }), 'New RFQ')
    await userEvent.clear(within(dialog).getByLabelText('Currency', { exact: false }))
    await userEvent.type(within(dialog).getByLabelText('Currency', { exact: false }), 'SYP')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('RFQ created')).toBeInTheDocument()
  })

  it('names the owner, and says "Unassigned" rather than leaving the cell blank', async () => {
    restore = mockFetch({
      '/api/v1/rfqs': listPage([
        RFQ_DRAFT,
        { ...RFQ_DRAFT, referenceCode: 'RFQ-2026-000009', ownerUserId: null, ownerName: null },
      ]),
    })

    renderPage(<RfqListPage />)

    // The control and the case together: an owned row shows a person, an unowned one shows the word.
    // A blank cell would read as missing data instead of as a row somebody should claim.
    expect(await screen.findByText('An Officer')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Unassigned' })).toBeInTheDocument()
  })

  it('asks the server for the filtered list rather than filtering the page it already has', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT]) }, recorded)

    renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    await userEvent.click(screen.getByRole('button', { name: 'Mine', pressed: false }))

    // "me" resolves server-side, which is why this page never needs the caller's own user id - and
    // filtering client-side would silently show only the first page's worth of matches.
    await waitFor(() => {
      expect(recorded.some((r) => r.url.includes('owner=me'))).toBe(true)
    })
    // The control: the unfiltered request carried no owner at all, so the assertion above is the
    // click and not a filter that was always being sent.
    expect(recorded[0].url).not.toContain('owner=')
  })

  it('says which list is empty, not just that something is', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<RfqListPage />)

    expect(await screen.findByText('No RFQs yet')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mine', pressed: false }))

    // "No RFQs yet" under a "Mine" filter would tell an officer their organization has none.
    expect(await screen.findByText('No RFQs are assigned to you')).toBeInTheDocument()
  })
})

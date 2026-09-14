// Two groups: FEAT-07.1's list and create flow, and the three things this screen used to say twice.
//
// The fixture carries exactly what the list projects - RfqListItemDto's reference, both titles, state, createdAt, and A-7's
// owner id and name - rather than the full aggregate, so a page that starts reading something the list no longer sends fails
// here rather than in production.
//
// The empty state shows with no RFQs. RFQs list with their reference code, title and real state badge - the real RfqState
// value rather than a client-derived label, so a reviewer can tell the lifecycle stage at a glance. Creating one toasts.
//
// The owner is NAMED, and an unowned row says "Unassigned" rather than leaving the cell blank: that test carries the case and
// its control together, an owned row showing a person and an unowned one showing the word, because a blank cell would read as
// missing data instead of as a row somebody should claim.
//
// THE FILTER asks the SERVER rather than narrowing the page already held. "me" resolves server-side, which is why this page
// never needs the caller's own user id - and filtering client-side would silently show only the first page's worth of
// matches. Its control is that the unfiltered request carried no owner at all, so the assertion is the click rather than a
// filter that was always being sent. The empty state says WHICH list is empty, because "No tenders yet" under a "Mine" filter
// would tell an officer their organization has none.
//
// A failure shows a retryable error rather than an empty screen, asserting two halves nothing did before: that the branch
// RENDERS, and that the control inside it does anything.
//
// THE SECOND GROUP is three things this screen said twice, and one column that said the same thing twenty-five times. The
// Rams audit counted five removable elements on this one list and scored the product 1 out of 3 on "as little design as
// possible"; two of the five - the footer Help and the second Search control - were closed earlier, and these are the rest.
//
// The page heading is not repeated as a card heading: one VISIBLE name for one list, where the page heading says "Tenders"
// and the card used to add "Tender list" directly beneath it - the table's own sr-only caption is the accessible name and is
// not a second visible heading. The table is named ONCE, for a screen reader as well as on screen: the name survives, which
// is what the audit's own warning was about, and it is carried once rather than by a caption AND a heading.
//
// And the owner column is DROPPED once the list is filtered to one owner. Unfiltered it answers "whose is this", which is a
// question a manager opens this screen with; filtered, every cell in it holds the same name by construction - twenty-five
// identical values beside the control that had just been used to make them identical.

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage, type RecordedRequest, expectRetryableFailure } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { RfqListPage } = await import('./RfqListPage')

const RFQ_DRAFT = {
  referenceCode: 'RFQ-2026-000001', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
  state: 'Draft', createdAt: '2026-08-30T09:00:00Z',
  ownerUserId: 'u-officer-1', ownerName: 'An Officer',
}

const RFQ_PUBLISHED = { ...RFQ_DRAFT, referenceCode: 'RFQ-2026-000002', titleEn: 'Published RFQ', state: 'Published' }

describe('RfqListPage', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('shows the empty state when no RFQs exist', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<RfqListPage />)

    expect(await screen.findByText('No tenders yet')).toBeInTheDocument()
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

    await userEvent.click(await screen.findByRole('button', { name: 'New tender' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Title (Arabic)', { exact: false }), 'طلب جديد')
    await userEvent.type(within(dialog).getByLabelText('Title (English)', { exact: false }), 'New tender')
    await userEvent.clear(within(dialog).getByLabelText('Currency', { exact: false }))
    await userEvent.type(within(dialog).getByLabelText('Currency', { exact: false }), 'SYP')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Tender created')).toBeInTheDocument()
  })

  it('names the owner, and says "Unassigned" rather than leaving the cell blank', async () => {
    restore = mockFetch({
      '/api/v1/rfqs': listPage([
        RFQ_DRAFT,
        { ...RFQ_DRAFT, referenceCode: 'RFQ-2026-000009', ownerUserId: null, ownerName: null },
      ]),
    })

    renderPage(<RfqListPage />)

    expect(await screen.findByText('An Officer')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Unassigned' })).toBeInTheDocument()
  })

  it('asks the server for the filtered list rather than filtering the page it already has', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT]) }, recorded)

    renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    await userEvent.click(screen.getByRole('button', { name: 'Mine', pressed: false }))

    await waitFor(() => {
      expect(recorded.some((r) => r.url.includes('owner=me'))).toBe(true)
    })
    expect(recorded[0].url).not.toContain('owner=')
  })

  it('says which list is empty, not just that something is', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([]) })

    renderPage(<RfqListPage />)

    expect(await screen.findByText('No tenders yet')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mine', pressed: false }))

    expect(await screen.findByText('No tenders are assigned to you')).toBeInTheDocument()
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/rfqs': { __status: 500 } }, recorded)

    renderPage(<RfqListPage />)

    await expectRetryableFailure('/api/v1/rfqs', recorded)
  })
})

describe('the tender list says each thing once', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('does not repeat the page heading as a card heading', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT, RFQ_PUBLISHED]) })

    renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    expect(screen.getAllByRole('heading', { name: 'Tenders' })).toHaveLength(1)
    expect(screen.queryByText('Tender list')).toBeNull()
    expect(screen.queryByRole('heading', { name: /tender list/i })).toBeNull()
  })

  it('names the table once, for a screen reader as well as on screen', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT, RFQ_PUBLISHED]) })

    const { container } = renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    const table = container.querySelector('table')
    expect(table).not.toBeNull()

    const named = table!.getAttribute('aria-labelledby') !== null || table!.querySelector('caption') !== null
    expect(named, 'the table must still have an accessible name').toBe(true)
    expect(
      table!.getAttribute('aria-labelledby') !== null && table!.querySelector('caption') !== null,
      'and only one of the two, or a reader hears it twice',
    ).toBe(false)
  })

  it('drops the owner column once the list is filtered to one owner', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT, RFQ_PUBLISHED]) })

    renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    expect(screen.getByRole('columnheader', { name: 'Owner' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mine' }))

    expect(screen.queryByRole('columnheader', { name: 'Owner' })).toBeNull()
  })
})

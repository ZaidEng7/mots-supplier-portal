import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage, type RecordedRequest, expectRetryableFailure } from '../../test/renderPage'

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

    expect(await screen.findByText('No tenders yet')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mine', pressed: false }))

    // "No tenders yet" under a "Mine" filter would tell an officer their organization has none.
    expect(await screen.findByText('No tenders are assigned to you')).toBeInTheDocument()
  })

  it('shows a retryable failure rather than an empty screen', async () => {
    // Two halves that nothing asserted before: that the error branch RENDERS, and that the control
    // inside it does anything. `asyncStateCoverage` proves the branch exists in the source; a retry
    // button wired to nothing looks identical to one that works.
    const recorded: RecordedRequest[] = []
    restore = mockFetch({ '/api/v1/rfqs': { __status: 500 } }, recorded)

    renderPage(<RfqListPage />)

    await expectRetryableFailure('/api/v1/rfqs', recorded)
  })
})

/**
 * Three things this screen said twice, and one column that said the same thing twenty-five times.
 *
 * <p>The Rams audit counted five removable elements on this one list and scored the product 1 out of 3
 * on "as little design as possible". Two of the five - the footer Help and the second Search control -
 * were closed earlier. These are the rest.</p>
 */
describe('the tender list says each thing once', () => {
  let restore: () => void
  afterEach(() => restore?.())

  it('does not repeat the page heading as a card heading', async () => {
    restore = mockFetch({ '/api/v1/rfqs': listPage([RFQ_DRAFT, RFQ_PUBLISHED]) })

    renderPage(<RfqListPage />)
    await screen.findByText('RFQ-2026-000001')

    // One VISIBLE name for one list. The page heading says "Tenders"; the card used to add
    // "Tender list" directly beneath it. The table's own sr-only caption is the accessible name and
    // is not a second visible heading.
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

    // The name survives - the audit's own warning was against deleting the caption and taking the
    // accessible name with it - and it is carried once rather than by a caption AND a heading.
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

    // Unfiltered it answers "whose is this", which is a question a manager opens this screen with.
    expect(screen.getByRole('columnheader', { name: 'Owner' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mine' }))

    // Filtered, every cell in it holds the same name by construction - twenty-five identical values
    // beside the control that had just been used to make them identical.
    expect(screen.queryByRole('columnheader', { name: 'Owner' })).toBeNull()
  })
})

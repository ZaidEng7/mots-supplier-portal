import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderPage, mockFetch, listPage } from '../../test/renderPage'

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tanstack/react-router')
  return { ...actual, Link: 'a' }
})

const { RfqListPage } = await import('./RfqListPage')

// The list endpoint projects `RfqListItemDto` - reference, both titles, state, createdAt - not the
// full aggregate. The fixture carries exactly those fields, so a page that starts reading something
// the list no longer sends fails here rather than in production.
const RFQ_DRAFT = {
  referenceCode: 'RFQ-2026-000001', titleAr: 'طلب تجريبي', titleEn: 'Sample RFQ',
  state: 'Draft', createdAt: '2026-08-30T09:00:00Z',
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
})
